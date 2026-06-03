// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class Logger : ILogger
{
    private const string FileNameFormat = "LibreHardwareMonitorLog-{0:yyyy-MM-dd}{1}.csv";

    private readonly IComputer _computer;
    private readonly TimeProvider _timeProvider;
    private readonly string _baseDirectory;
    // Guards the _sensors/_identifiers pair: the timer thread reassigns them on rotation while background
    // hardware-discovery threads read/write them via SensorAdded/SensorRemoved (Computer.HardwareAdded now fires
    // off the caller thread once any deferred-detection flag is active).
    private readonly object _sync = new();
    private DateTime _day = DateTime.MinValue;
    private string _fileName = "";
    private string[]? _identifiers;
    private DateTime _lastLoggedTime = DateTime.MinValue;
    private ISensor?[]? _sensors;

    public Logger(IComputer computer)
        : this(computer, TimeProvider.System, AppContext.BaseDirectory)
    {
    }

    // Test seam: lets unit tests drive the clock (interval gating, daily rotation) and redirect output to a temp directory.
    internal Logger(IComputer computer, TimeProvider timeProvider, string baseDirectory)
    {
        _computer = computer;
        _timeProvider = timeProvider;
        _baseDirectory = baseDirectory;
        _computer.HardwareAdded += HardwareAdded;
        _computer.HardwareRemoved += HardwareRemoved;
    }

    public LoggerFileRotation FileRotationMethod { get; set; } = LoggerFileRotation.PerSession;

    public TimeSpan LoggingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public void Log()
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        if (_lastLoggedTime + LoggingInterval - TimeSpan.FromMilliseconds(500) > now)
            return;

        switch (FileRotationMethod)
        {
            case LoggerFileRotation.PerSession:
                if (!File.Exists(_fileName) || now - _lastLoggedTime > LoggingInterval + TimeSpan.FromMilliseconds(100))
                {
                    uint sessionNumber = 1;
                    do
                    {
                        _fileName = GetFileName(now, sessionNumber);
                        sessionNumber++;
                    } while (File.Exists(_fileName));

                    CreateNewLogFile();
                }
                break;

            case LoggerFileRotation.Daily:
                if (_day != now.Date || !File.Exists(_fileName))
                {
                    _day = now.Date;
                    _fileName = GetFileName(_day);
                    if (!OpenExistingLogFile())
                        CreateNewLogFile();
                }
                break;
        }

        // Snapshot the sensor array under the lock so a concurrent SensorAdded/SensorRemoved, or a rotation that swaps
        // in a different-length array, on a discovery thread cannot tear the read below. Element reads stay safe: a
        // reference write to a slot is atomic, so each read yields either a sensor or null.
        ISensor?[]? sensors;
        lock (_sync)
            sensors = _sensors;

        try
        {
            using StreamWriter writer = new(new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            writer.Write(now.ToString("G", CultureInfo.InvariantCulture));
            writer.Write(",");

            if (sensors != null)
            {
                for (int i = 0; i < sensors.Length; i++)
                {
                    float? value = sensors[i]?.Value;
                    if (value.HasValue)
                        writer.Write(value.Value.ToString("R", CultureInfo.InvariantCulture));

                    writer.Write(i < sensors.Length - 1 ? "," : Environment.NewLine);
                }
            }
        }
        catch (IOException)
        {
        }

        _lastLoggedTime = now;
    }

    private string GetFileName(DateTime date, uint sessionNumber = 0)
    {
        return Path.Combine(_baseDirectory, string.Format(FileNameFormat, date, sessionNumber == 0 ? "" : "-" + sessionNumber));
    }

    private void CreateNewLogFile()
    {
        IList<ISensor> sensors = new List<ISensor>();
        SensorVisitor visitor = new(sensor => sensors.Add(sensor));
        visitor.VisitComputer(_computer);

        ISensor?[] sensorArray = sensors.Cast<ISensor?>().ToArray();
        string[] identifiers = sensors.Select(sensor => sensor.Identifier.ToString()).ToArray();

        // Publish the paired arrays together under the lock so a concurrent SensorAdded/SensorRemoved never observes
        // mismatched lengths. VisitComputer and the file write run outside the lock to avoid coupling with Computer's
        // own locks.
        lock (_sync)
        {
            _sensors = sensorArray;
            _identifiers = identifiers;
        }

        using StreamWriter writer = new(_fileName, false);
        writer.Write(",");
        writer.WriteLine(string.Join(",", identifiers));
        writer.Write("Time,");
        writer.WriteLine(string.Join(",", sensors.Select(sensor => $"\"{sensor.Name}\"")));
    }

    private bool OpenExistingLogFile()
    {
        if (!File.Exists(_fileName))
            return false;

        string[] identifiers;
        try
        {
            using StreamReader reader = new(_fileName);
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                return false;

            identifiers = line.Split(',').Skip(1).ToArray();
        }
        catch
        {
            return false;
        }

        if (identifiers.Length == 0)
            return false;

        // Build into local arrays, then publish the pair together under the lock, so a discovery thread reading
        // _sensors/_identifiers never observes a half-populated or length-mismatched pair.
        ISensor?[] sensors = new ISensor?[identifiers.Length];
        SensorVisitor visitor = new(sensor =>
        {
            for (int i = 0; i < identifiers.Length; i++)
            {
                if (sensor.Identifier.ToString() == identifiers[i])
                    sensors[i] = sensor;
            }
        });
        visitor.VisitComputer(_computer);

        lock (_sync)
        {
            _identifiers = identifiers;
            _sensors = sensors;
        }

        return true;
    }

    private void HardwareAdded(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void HardwareRemoved(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (ISensor sensor in hardware.Sensors)
            SensorRemoved(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareRemoved(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        lock (_sync)
        {
            if (_sensors == null || _identifiers == null)
                return;

            // _sensors and _identifiers are always published together with equal lengths, so indexing both is safe.
            for (int i = 0; i < _sensors.Length; i++)
            {
                if (sensor.Identifier.ToString() == _identifiers[i])
                    _sensors[i] = sensor;
            }
        }
    }

    private void SensorRemoved(ISensor sensor)
    {
        lock (_sync)
        {
            if (_sensors == null)
                return;

            for (int i = 0; i < _sensors.Length; i++)
            {
                if (sensor == _sensors[i])
                    _sensors[i] = null;
            }
        }
    }
}
