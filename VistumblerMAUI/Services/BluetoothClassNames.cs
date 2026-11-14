namespace VistumblerMAUI.Services;

/// <summary>
/// Bluetooth device class names as WiGLE WiFi Wardriving writes them (its DEVICE_TYPE_LEGEND, BSD 3-clause; see
/// THIRD-PARTY-NOTICES.md), so a device scanned on Android or Windows is recorded with the same name WiGLE and WifiDB
/// use. Keyed by the Class of Device's major and minor fields, the code Android's BluetoothClass.DeviceClass gives and
/// Windows' major and minor class make: (major &lt;&lt; 8) | (minor &lt;&lt; 2).
/// </summary>
public static class BluetoothClassNames
{
    /// <summary>The name for a device class code, falling back to the major class when the minor one isn't listed.</summary>
    public static string For(int deviceClass)
    {
        deviceClass &= 0x1FFC;   // the major and minor fields, without the service bits
        if (Legend.TryGetValue(deviceClass, out var name)) return name;
        return (deviceClass & 0x1F00) switch
        {
            0x0400 => "A/V",
            0x0100 => "Computer",
            0x0900 => "Health",
            0x0600 => "Imaging",
            0x0300 => "Networking",
            0x0500 => "Peripheral",
            0x0200 => "Phone",
            0x0800 => "Toy",
            0x0700 => "Wearable",
            0x0000 => "Misc",
            _ => "Uncategorized",
        };
    }

    /// <summary>The code for a Class of Device's major and minor fields.</summary>
    public static int Code(int major, int minor) => ((major & 0x1F) << 8) | ((minor & 0x3F) << 2);

    private static readonly Dictionary<int, string> Legend = new()
    {
        [0x0000] = "Misc",
        [0x0434] = "Camcorder",
        [0x0420] = "Car Audio",
        [0x0408] = "Handsfree",
        [0x0418] = "Headphones",
        [0x0428] = "HiFi",
        [0x0414] = "Speaker",
        [0x0410] = "Mic",
        [0x041C] = "Portable Audio",
        [0x0424] = "Settop",
        [0x0400] = "A/V",
        [0x042C] = "VCR",
        [0x0430] = "Camera",
        [0x0440] = "Videoconf",
        [0x043C] = "Display/Speaker",
        [0x0448] = "AV Toy",
        [0x0438] = "Monitor",
        [0x0104] = "Desktop",
        [0x0110] = "PDA",
        [0x010C] = "Laptop",
        [0x0114] = "Palm",
        [0x0108] = "Server",
        [0x0100] = "Computer",
        [0x0118] = "Wearable Computer",
        [0x0904] = "Blood Pressure",
        [0x091C] = "Health Display",
        [0x0910] = "Glucose",
        [0x0914] = "PulseOxy",
        [0x0918] = "Pulse",
        [0x0908] = "Thermometer",
        [0x0900] = "Health",
        [0x090C] = "Scale",
        [0x0540] = "Keyboard",
        [0x05C0] = "Keyboard+p",
        [0x0500] = "Keyboard !p",
        [0x0580] = "Pointer",
        [0x0204] = "Cellphone",
        [0x0208] = "Cordless Phone",
        [0x0214] = "ISDN",
        [0x0210] = "Modem/GW",
        [0x020C] = "Smartphone",
        [0x0200] = "Phone",
        [0x0810] = "Controller",
        [0x080C] = "Doll",
        [0x0814] = "Game",
        [0x0804] = "Robot",
        [0x0800] = "Toy",
        [0x0808] = "Vehicle",
        [0x0714] = "Glasses",
        [0x0710] = "Helmet",
        [0x070C] = "Jacket",
        [0x0708] = "Pager",
        [0x0700] = "Wearable",
        [0x0704] = "Watch",
        [0x1F00] = "Uncategorized",
    };
}
