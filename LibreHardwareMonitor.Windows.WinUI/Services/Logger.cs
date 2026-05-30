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

public sealed class Logger
{
    private const string FileNameFormat = "LibreHardwareMonitorLog-{0:yyyy-MM-dd}{1}.csv";

    private readonly IComputer _computer;
    private DateTime _day = DateTime.MinValue;
    private string _fileName = "";
    private string[]? _identifiers;
    private DateTime _lastLoggedTime = DateTime.MinValue;
    private ISensor?[]? _sensors;

    public Logger(IComputer computer)
    {
        _computer = computer;
        _computer.HardwareAdded += HardwareAdded;
        _computer.HardwareRemoved += HardwareRemoved;
    }

    public LoggerFileRotation FileRotationMethod { get; set; } = LoggerFileRotation.PerSession;

    public TimeSpan LoggingInterval { get; set; } = TimeSpan.FromSeconds(1);

    public void Log()
    {
        DateTime now = DateTime.Now;
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
                        _fileName = GetFileName(DateTime.Now, sessionNumber);
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

        try
        {
            using StreamWriter writer = new(new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            writer.Write(now.ToString("G", CultureInfo.InvariantCulture));
            writer.Write(",");

            if (_sensors != null)
            {
                for (int i = 0; i < _sensors.Length; i++)
                {
                    float? value = _sensors[i]?.Value;
                    if (value.HasValue)
                        writer.Write(value.Value.ToString("R", CultureInfo.InvariantCulture));

                    writer.Write(i < _sensors.Length - 1 ? "," : Environment.NewLine);
                }
            }
        }
        catch (IOException)
        {
        }

        _lastLoggedTime = now;
    }

    private static string GetFileName(DateTime date, uint sessionNumber = 0)
    {
        return Path.Combine(AppContext.BaseDirectory, string.Format(FileNameFormat, date, sessionNumber == 0 ? "" : "-" + sessionNumber));
    }

    private void CreateNewLogFile()
    {
        IList<ISensor> sensors = new List<ISensor>();
        SensorVisitor visitor = new(sensor => sensors.Add(sensor));
        visitor.VisitComputer(_computer);

        _sensors = sensors.Cast<ISensor?>().ToArray();
        _identifiers = sensors.Select(sensor => sensor.Identifier.ToString()).ToArray();

        using StreamWriter writer = new(_fileName, false);
        writer.Write(",");
        writer.WriteLine(string.Join(",", _identifiers));
        writer.Write("Time,");
        writer.WriteLine(string.Join(",", sensors.Select(sensor => $"\"{sensor.Name}\"")));
    }

    private bool OpenExistingLogFile()
    {
        if (!File.Exists(_fileName))
            return false;

        try
        {
            using StreamReader reader = new(_fileName);
            string? line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
                return false;

            _identifiers = line.Split(',').Skip(1).ToArray();
        }
        catch
        {
            _identifiers = null;
            return false;
        }

        if (_identifiers.Length == 0)
        {
            _identifiers = null;
            return false;
        }

        _sensors = new ISensor?[_identifiers.Length];
        SensorVisitor visitor = new(sensor =>
        {
            for (int i = 0; i < _identifiers.Length; i++)
            {
                if (sensor.Identifier.ToString() == _identifiers[i])
                    _sensors[i] = sensor;
            }
        });
        visitor.VisitComputer(_computer);
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
        if (_sensors == null || _identifiers == null)
            return;

        for (int i = 0; i < _sensors.Length; i++)
        {
            if (sensor.Identifier.ToString() == _identifiers[i])
                _sensors[i] = sensor;
        }
    }

    private void SensorRemoved(ISensor sensor)
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
