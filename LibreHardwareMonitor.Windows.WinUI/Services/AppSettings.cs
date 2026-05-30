// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using LibreHardwareMonitor.Hardware;
using Windows.UI;

namespace LibreHardwareMonitor.Windows.WinUI.Services;

public sealed class AppSettings : ISettings
{
    private readonly IDictionary<string, string> _settings = new Dictionary<string, string>();

    private AppSettings(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }

    public static AppSettings LoadDefault()
    {
        string fileName = Path.ChangeExtension(Environment.ProcessPath, ".config")
                          ?? Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitor.Windows.WinUI.config");
        AppSettings settings = new(fileName);
        settings.Load();
        return settings;
    }

    public bool Contains(string name)
    {
        return _settings.ContainsKey(name);
    }

    public string GetValue(string name, string value)
    {
        return _settings.TryGetValue(name, out string? result) ? result : value;
    }

    public int GetValue(string name, int value)
    {
        return _settings.TryGetValue(name, out string? result) && int.TryParse(result, out int parsedValue)
            ? parsedValue
            : value;
    }

    public float GetValue(string name, float value)
    {
        return _settings.TryGetValue(name, out string? result)
               && float.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)
            ? parsedValue
            : value;
    }

    public double GetValue(string name, double value)
    {
        return _settings.TryGetValue(name, out string? result)
               && double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue)
            ? parsedValue
            : value;
    }

    public bool GetValue(string name, bool value)
    {
        return _settings.TryGetValue(name, out string? result) ? result == "true" : value;
    }

    public Color GetValue(string name, Color value)
    {
        if (_settings.TryGetValue(name, out string? result)
            && uint.TryParse(result, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedValue))
        {
            return Color.FromArgb(
                (byte)((parsedValue >> 24) & 0xff),
                (byte)((parsedValue >> 16) & 0xff),
                (byte)((parsedValue >> 8) & 0xff),
                (byte)(parsedValue & 0xff));
        }

        return value;
    }

    public void SetValue(string name, string value)
    {
        _settings[name] = value;
    }

    public void SetValue(string name, int value)
    {
        _settings[name] = value.ToString(CultureInfo.InvariantCulture);
    }

    public void SetValue(string name, float value)
    {
        _settings[name] = value.ToString(CultureInfo.InvariantCulture);
    }

    public void SetValue(string name, double value)
    {
        _settings[name] = value.ToString(CultureInfo.InvariantCulture);
    }

    public void SetValue(string name, bool value)
    {
        _settings[name] = value ? "true" : "false";
    }

    public void SetValue(string name, Color value)
    {
        uint argb = (uint)((value.A << 24) | (value.R << 16) | (value.G << 8) | value.B);
        _settings[name] = argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    public void Remove(string name)
    {
        _settings.Remove(name);
    }

    public void Load()
    {
        XmlDocument doc = new();
        try
        {
            doc.Load(FileName);
        }
        catch
        {
            if (!TryLoadBackup(doc))
                return;
        }

        XmlNodeList list = doc.GetElementsByTagName("appSettings");
        foreach (XmlNode node in list)
        {
            if (node.ParentNode?.Name != "configuration" || node.ParentNode.ParentNode is not XmlDocument)
                continue;

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name != "add")
                    continue;

                XmlAttribute? keyAttribute = child.Attributes?["key"];
                XmlAttribute? valueAttribute = child.Attributes?["value"];
                if (keyAttribute?.Value != null && valueAttribute?.Value != null)
                    _settings[keyAttribute.Value] = valueAttribute.Value;
            }
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileName) ?? AppContext.BaseDirectory);

        XmlDocument doc = new();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
        XmlElement configuration = doc.CreateElement("configuration");
        doc.AppendChild(configuration);
        XmlElement appSettings = doc.CreateElement("appSettings");
        configuration.AppendChild(appSettings);

        foreach (KeyValuePair<string, string> setting in _settings)
        {
            XmlElement add = doc.CreateElement("add");
            add.SetAttribute("key", setting.Key);
            add.SetAttribute("value", setting.Value);
            appSettings.AppendChild(add);
        }

        byte[] file;
        using (MemoryStream memory = new())
        {
            using (StreamWriter writer = new(memory, Encoding.UTF8, leaveOpen: true))
                doc.Save(writer);
            file = memory.ToArray();
        }

        string backupFileName = FileName + ".backup";
        if (File.Exists(FileName))
        {
            TryDelete(backupFileName);
            TryMove(FileName, backupFileName);
        }

        using (FileStream stream = new(FileName, FileMode.Create, FileAccess.Write))
            stream.Write(file, 0, file.Length);

        TryDelete(backupFileName);
    }

    private bool TryLoadBackup(XmlDocument doc)
    {
        TryDelete(FileName);
        string backupFileName = FileName + ".backup";
        try
        {
            doc.Load(backupFileName);
            return true;
        }
        catch
        {
            TryDelete(backupFileName);
            return false;
        }
    }

    private static void TryDelete(string fileName)
    {
        try
        {
            File.Delete(fileName);
        }
        catch
        {
        }
    }

    private static void TryMove(string sourceFileName, string destinationFileName)
    {
        try
        {
            File.Move(sourceFileName, destinationFileName);
        }
        catch
        {
        }
    }
}
