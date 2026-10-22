using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace RazerProlapse;

public partial class MainWindow : Window
{
    private const int RazerVendorId = 0x1532;
    private static readonly byte[] TransactionIds = [0x1F, 0x3F, 0xFF];

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    public MainWindow()
    {
        InitializeComponent();
        InitializeMouseState();
    }

    private static List<HidDevice> GetRazerControlInterfaces()
    {
        var devices = new List<HidDevice>();
        foreach (var device in DeviceList.Local.GetHidDevices(RazerVendorId))
        {
            try
            {
                if (device.GetMaxFeatureReportLength() >= 90)
                {
                    devices.Add(device);
                }
            }
            catch
            {
                // Ignore restricted interfaces
            }
        }
        return devices;
    }

    private void InitializeMouseState()
    {
        var devices = GetRazerControlInterfaces();
        if (devices.Count == 0)
        {
            StatusText.Text = "No Razer mouse found";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            return;
        }

        string deviceName = GetFriendlyName(devices[0]);
        if (TryReadCurrentDpi(devices, out int currentDpi) && currentDpi is >= 100 and <= 16000)
        {
            DpiSlider.Value = currentDpi;
            DpiValueText.Text = currentDpi.ToString();
        }

        StatusText.Text = deviceName;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));
    }

    private static string GetFriendlyName(HidDevice device)
    {
        try
        {
            var name = device.GetProductName();
            return string.IsNullOrWhiteSpace(name) ? $"Razer (0x{device.ProductID:X4})" : name.Trim();
        }
        catch
        {
            return $"Razer (0x{device.ProductID:X4})";
        }
    }

    private static bool TryReadCurrentDpi(List<HidDevice> devices, out int dpi)
    {
        dpi = 0;
        foreach (var device in devices)
        {
            try
            {
                using SafeFileHandle handle = CreateFile(
                    device.DevicePath,
                    0,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);

                if (handle.IsInvalid)
                {
                    continue;
                }

                byte[] packet = new byte[91];
                packet[0] = 0x00;
                packet[1] = 0x00;
                packet[2] = 0x1F;
                packet[6] = 0x07;
                packet[7] = 0x04;
                packet[8] = 0x85; // Get DPI
                packet[9] = 0x01;
                packet[89] = ComputeChecksum(packet);

                if (HidD_SetFeature(handle, packet, packet.Length))
                {
                    Thread.Sleep(15);
                    byte[] response = new byte[91];
                    if (HidD_GetFeature(handle, response, response.Length) && response[1] == 0x02)
                    {
                        int xDpi = (response[10] << 8) | response[11];
                        if (xDpi >= 100)
                        {
                            dpi = xDpi;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore read errors
            }
        }
        return false;
    }

    private void DpiSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DpiValueText != null)
        {
            DpiValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
        }
    }

    private void DpiSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ApplyDpi((int)Math.Round(DpiSlider.Value));
    }

    private void DpiSlider_KeyUp(object sender, KeyEventArgs e)
    {
        ApplyDpi((int)Math.Round(DpiSlider.Value));
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagStr } && int.TryParse(tagStr, out int presetDpi))
        {
            DpiSlider.Value = presetDpi;
            ApplyDpi(presetDpi);
        }
    }

    private static bool SendFeatureReportsToDevice(HidDevice device, int dpi)
    {
        bool sent = false;

        try
        {
            using HidStream stream = device.Open();
            foreach (byte txId in TransactionIds)
            {
                stream.SetFeature(BuildSetDpiPacket(dpi, txId));
                Thread.Sleep(5);
                stream.SetFeature(BuildSetDpiStagesPacket(dpi, txId));
                Thread.Sleep(5);
                sent = true;
            }
            if (sent)
            {
                return true;
            }
        }
        catch
        {
            // Fallback to CreateFile with dwDesiredAccess = 0 when Windows locks the mouse collection
        }

        try
        {
            using SafeFileHandle handle = CreateFile(
                device.DevicePath,
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                foreach (byte txId in TransactionIds)
                {
                    byte[] legacyPacket = BuildSetDpiPacket(dpi, txId);
                    if (HidD_SetFeature(handle, legacyPacket, legacyPacket.Length))
                    {
                        sent = true;
                    }
                    Thread.Sleep(5);

                    byte[] stagesPacket = BuildSetDpiStagesPacket(dpi, txId);
                    if (HidD_SetFeature(handle, stagesPacket, stagesPacket.Length))
                    {
                        sent = true;
                    }
                    Thread.Sleep(5);
                }
            }
        }
        catch
        {
            // Ignore interfaces that reject feature reports
        }

        return sent;
    }

    private void ApplyDpi(int dpi)
    {
        var devices = GetRazerControlInterfaces();
        if (devices.Count == 0)
        {
            StatusText.Text = "No Razer mouse found";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            return;
        }

        bool anySuccess = false;
        string? activeDeviceName = null;

        foreach (var device in devices)
        {
            if (SendFeatureReportsToDevice(device, dpi))
            {
                anySuccess = true;
                activeDeviceName ??= GetFriendlyName(device);
            }
        }

        if (anySuccess)
        {
            StatusText.Text = $"{activeDeviceName ?? "Razer Mouse"} · {dpi} DPI";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        else
        {
            StatusText.Text = "Failed to set DPI";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
    }

    public static byte[] BuildSetDpiPacket(int dpi, byte txId)
    {
        byte[] packet = new byte[91];
        packet[0] = 0x00;
        packet[1] = 0x00;
        packet[2] = txId;
        packet[6] = 0x07;
        packet[7] = 0x04;
        packet[8] = 0x05;
        packet[9] = 0x01;
        packet[10] = (byte)((dpi >> 8) & 0xFF);
        packet[11] = (byte)(dpi & 0xFF);
        packet[12] = (byte)((dpi >> 8) & 0xFF);
        packet[13] = (byte)(dpi & 0xFF);
        packet[89] = ComputeChecksum(packet);
        return packet;
    }

    public static byte[] BuildSetDpiStagesPacket(int dpi, byte txId)
    {
        byte[] packet = new byte[91];
        packet[0] = 0x00;
        packet[1] = 0x00;
        packet[2] = txId;
        packet[6] = 0x0A;
        packet[7] = 0x04;
        packet[8] = 0x26;
        packet[9] = 0x01;
        packet[10] = 0x01;
        packet[11] = 0x01;
        packet[12] = 0x01;
        packet[13] = (byte)((dpi >> 8) & 0xFF);
        packet[14] = (byte)(dpi & 0xFF);
        packet[15] = (byte)((dpi >> 8) & 0xFF);
        packet[16] = (byte)(dpi & 0xFF);
        packet[17] = 0x00;
        packet[18] = 0x00;
        packet[89] = ComputeChecksum(packet);
        return packet;
    }

    private static byte ComputeChecksum(byte[] packet)
    {
        byte crc = 0;
        for (int i = 3; i <= 88; i++)
        {
            crc ^= packet[i];
        }
        return crc;
    }
}
