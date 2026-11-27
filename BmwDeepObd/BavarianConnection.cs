#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Linq;
using Android.Content;
using Android.Hardware.Usb;
using Android.Bluetooth;
using Android.Content.PM;
using EdiabasLib;
namespace BmwDeepObd
{
    internal sealed class BavarianEndpoint
    {
        public UsbDevice Usb;
        public string Address, Name;
        public bool Bluetooth=>Address!=null;
        public string DeviceName=>Bluetooth?"bt:"+Address:Usb.DeviceName;
    }
    internal static class BavarianConnection
    {
        private static ISharedPreferences Pref(Context c)=>c.GetSharedPreferences("bavarian_connection",FileCreationMode.Private);
        internal static bool IsBluetooth(Context c)=>Pref(c).GetString("mode","usb")=="bt";
        internal static bool HasChoice(Context c)=>Pref(c).Contains("mode");
        internal static void SelectUsb(Context c){Pref(c).Edit().PutString("mode","usb").Remove("address").Apply();Log(c,"Selected K+DCAN");}
        internal static void SelectBluetooth(Context c,string address){Pref(c).Edit().PutString("mode","bt").PutString("address",address).Apply();Log(c,"Selected experimental Bluetooth");}
        internal static bool BtPermission(Context c)=>!OperatingSystem.IsAndroidVersionAtLeast(31)||(c.CheckSelfPermission("android.permission.BLUETOOTH_CONNECT")==Permission.Granted&&c.CheckSelfPermission("android.permission.BLUETOOTH_SCAN")==Permission.Granted);
        internal static BluetoothDevice[] Paired(Context c)
        {
            if(!BtPermission(c))return Array.Empty<BluetoothDevice>();
            var adapter=(c.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
            if(adapter==null||!adapter.IsEnabled)return Array.Empty<BluetoothDevice>();
            return adapter.BondedDevices?.Where(d=>d.Type==BluetoothDeviceType.Classic||d.Type==BluetoothDeviceType.Dual).ToArray()??Array.Empty<BluetoothDevice>();
        }
        internal static BavarianEndpoint Endpoint(Context c,UsbManager manager)
        {
            if(IsBluetooth(c))
            {
                string address=Pref(c).GetString("address","");
                var device=Paired(c).FirstOrDefault(d=>d.Address==address);
                return device==null?null:new BavarianEndpoint{Address=device.Address,Name=device.Name??"Bluetooth"};
            }
            if(manager==null)return null;
            var drivers=EdFtdiInterface.GetDriverList(manager);
            return drivers.Count==1?new BavarianEndpoint{Usb=drivers[0].Device,Name="K+DCAN"}:null;
        }
        internal static bool Ready(Context c,UsbManager manager,BavarianEndpoint endpoint)=>endpoint!=null&&(endpoint.Bluetooth?BtPermission(c):manager!=null&&manager.HasPermission(endpoint.Usb));
        internal static void Configure(Context c,UsbManager manager,BavarianEndpoint endpoint,EdInterfaceObd transport)
        {
            if(endpoint.Bluetooth)
            {
                transport.ComPort=EdBluetoothInterface.PortId+":"+endpoint.Address+"#"+EdBluetoothInterface.Elm327Tag;
                transport.ConnectParameter=new EdBluetoothInterface.ConnectParameterType(null,false,false,()=>c.ApplicationContext);
            }
            else
            {
                transport.ComPort=EdFtdiInterface.PortId+"0";
                transport.ConnectParameter=new EdFtdiInterface.ConnectParameterType(manager);
            }
        }
        private static readonly object LogLock=new object();
        internal static void Log(Context c,string message)
        {
            try{lock(LogLock){var path=System.IO.Path.Combine(c.FilesDir.AbsolutePath,"bavarian-connection.txt");if(File.Exists(path)&&new FileInfo(path).Length>65536)File.Delete(path);File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+"\n");}}catch(Exception){}
        }
        internal static string History(Context c)
        {
            try{lock(LogLock){var path=System.IO.Path.Combine(c.FilesDir.AbsolutePath,"bavarian-connection.txt");return File.Exists(path)?File.ReadAllText(path):"—";}}catch(Exception){return "—";}
        }
    }
}
#endif
