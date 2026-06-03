// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class Logger
{
    private const string FileNameFormat = "LibreHardwareMonitorLog-{0:yyyy-MM-dd}{1}.csv";

    private readonly IComputer _computer;

    private DateTime _day = DateTime.MinValue;
    private string _fileName;
    private string[] _identifiers;
    private ISensor[] _sensors;
    private DateTime _lastLoggedTime = DateTime.MinValue;
    // Guards the _sensors/_identifiers pair: when a defer env var is set, Computer.HardwareAdded can now fire on a
    // background thread, so SensorAdded/SensorRemoved can run concurrently with the timer thread's rotation. Mirrors
    // the WinUI Logger fix.
    private readonly object _sync = new object();

    public LoggerFileRotation FileRotationMethod = LoggerFileRotation.PerSession;

    public Logger(IComputer computer)
    {
        _computer = computer;
        _computer.HardwareAdded += HardwareAdded;
        _computer.HardwareRemoved += HardwareRemoved;
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

    private void HardwareAdded(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        lock (_sync)
        {
            if (_sensors == null)
                return;

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

    private static string GetFileName(DateTime date, uint sessionNumber = 0)
    {
        return AppDomain.CurrentDomain.BaseDirectory + Path.DirectorySeparatorChar
            + string.Format(FileNameFormat, date, sessionNumber == 0 ? "" : "-" + sessionNumber);
    }

    private bool OpenExistingLogFile()
    {
        if (!File.Exists(_fileName))
            return false;

        string[] identifiers;
        try
        {
            string line;
            using (StreamReader reader = new StreamReader(_fileName))
                line = reader.ReadLine();

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

        ISensor[] sensors = new ISensor[identifiers.Length];
        SensorVisitor visitor = new SensorVisitor(sensor =>
        {
            for (int i = 0; i < identifiers.Length; i++)
                if (sensor.Identifier.ToString() == identifiers[i])
                    sensors[i] = sensor;
        });
        visitor.VisitComputer(_computer);

        lock (_sync)
        {
            _identifiers = identifiers;
            _sensors = sensors;
        }

        return true;
    }

    private void CreateNewLogFile()
    {
        IList<ISensor> list = new List<ISensor>();
        SensorVisitor visitor = new SensorVisitor(sensor =>
        {
            list.Add(sensor);
        });
        visitor.VisitComputer(_computer);
        ISensor[] sensors = list.ToArray();
        string[] identifiers = sensors.Select(s => s.Identifier.ToString()).ToArray();

        lock (_sync)
        {
            _sensors = sensors;
            _identifiers = identifiers;
        }

        using (StreamWriter writer = new StreamWriter(_fileName, false))
        {
            writer.Write(",");
            for (int i = 0; i < sensors.Length; i++)
            {
                writer.Write(sensors[i].Identifier);
                if (i < sensors.Length - 1)
                    writer.Write(",");
                else
                    writer.WriteLine();
            }

            writer.Write("Time,");
            for (int i = 0; i < sensors.Length; i++)
            {
                writer.Write('"');
                writer.Write(sensors[i].Name);
                writer.Write('"');
                if (i < sensors.Length - 1)
                    writer.Write(",");
                else
                    writer.WriteLine();
            }
        }
    }

    public TimeSpan LoggingInterval { get; set; }

    public void Log()
    {
        DateTime now = DateTime.Now;

        if (_lastLoggedTime + LoggingInterval - new TimeSpan(5000000) > now)
            return;

        switch (FileRotationMethod)
        {
            case LoggerFileRotation.PerSession:
                // Create file if it does not exist or the logging interval has passed (+ some margin)
                if (!File.Exists(_fileName) || now - _lastLoggedTime > (LoggingInterval + TimeSpan.FromMilliseconds(100)))
                {
                    uint sessionNumber = 1;
                    do {
                        _fileName = GetFileName(DateTime.Now, sessionNumber);
                        sessionNumber++;
                    } while (File.Exists(_fileName));
                    CreateNewLogFile();
                }
                break;
            case LoggerFileRotation.Daily:
                // Create a new file if the day has changed or the file does not exist
                if (_day != now.Date || !File.Exists(_fileName))
                {
                    _day = now.Date;
                    _fileName = GetFileName(_day);
                    if (!OpenExistingLogFile())
                        CreateNewLogFile();
                }
                break;
        }

        ISensor[] sensors;
        lock (_sync)
            sensors = _sensors;

        try
        {
            using (StreamWriter writer = new StreamWriter(new FileStream(_fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
            {
                writer.Write(now.ToString("G", CultureInfo.InvariantCulture));
                writer.Write(",");
                if (sensors != null)
                {
                    for (int i = 0; i < sensors.Length; i++)
                    {
                        if (sensors[i] != null)
                        {
                            float? value = sensors[i].Value;
                            if (value.HasValue)
                                writer.Write(value.Value.ToString("R", CultureInfo.InvariantCulture));
                        }
                        if (i < sensors.Length - 1)
                            writer.Write(",");
                        else
                            writer.WriteLine();
                    }
                }
            }
        }
        catch (IOException) { }

        _lastLoggedTime = now;
    }
}