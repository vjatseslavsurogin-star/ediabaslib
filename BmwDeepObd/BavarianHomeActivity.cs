#if BAVARIAN_USB_BASELINE
using System;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace BmwDeepObd
{
    // Hardware preflight only. No ECU messages are transmitted by this screen.
    [Android.App.Activity(Label = "@string/app_name", MainLauncher = true,
        Exported = true, Name = ActivityCommon.AppNameSpace + "." + nameof(BavarianHomeActivity))]
    public class BavarianHomeActivity : AppCompatActivity
    {
        private LinearLayout _body;
        private TextView _usb;
        private TextView _ecu;
        private Button _diagnostics, _transmission, _developer, _library, _faults, _liveEngine, _liveEgs, _injectors;
        private bool _ru;
        private string T(string en, string ru) => _ru ? ru : en;
        private int Dp(int value) => (int)(value * Resources.DisplayMetrics.Density + 0.5f);
        private static Color C(string value) => Color.ParseColor(value);

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _ru = BavarianLanguage.IsRussian(this);
            BavarianLanguage.EnsureEngineEnglish();
            SupportActionBar?.Hide();
            Window.SetStatusBarColor(C("#0C1422"));
            Window.SetNavigationBarColor(C("#0C1422"));
            var scroll = new ScrollView(this) { FillViewport = true };
            scroll.SetFitsSystemWindows(true);
            scroll.SetBackgroundColor(C("#0C1422"));
            _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _body.SetPadding(Dp(20), Dp(24), Dp(20), Dp(24));
            scroll.AddView(_body);
            SetContentView(scroll);
            AddText("BAVARIAN", 32, "#F2F6FC");
            AddText("DIAGNOSTICS  /  1.4 EXTENDED LIVE", 15, "#67B3FF");
            AddText("BMW E60 · M57 · K+DCAN USB", 16, "#C2CDDD");
            AddText(T("READ ONLY · FIRST VEHICLE TEST", "ТОЛЬКО ЧТЕНИЕ · ПЕРВЫЙ ТЕСТ"), 13, "#67B3FF");
            AddText(T("CONNECTION","ПОДКЛЮЧЕНИЕ"),13,"#67B3FF",18);
            _usb = AddText("", 14, "#F2F6FC");
            _ecu = AddText("", 12, "#C2CDDD");
            AddButton(T("Refresh USB","Обновить USB"),(s,e)=>RefreshStatus());
            AddText(T("DIAGNOSTICS","ДИАГНОСТИКА"),13,"#67B3FF",24);
            var row1=TileRow();
            _faults=Tile(row1,T("Fault codes\nDDE + EGS","Ошибки\nDDE + EGS"),(s,e)=>OpenVehicle("faults"));
            _liveEngine=Tile(row1,T("Live Data\nEngine","Live Data\nДвигатель"),(s,e)=>OpenVehicle("live_engine"));
            var row2=TileRow();
            _liveEgs=Tile(row2,T("Live Data\nTransmission","Live Data\nАКПП"),(s,e)=>OpenVehicle("live_egs"));
            Tile(row2,T("DEMO\nOffline dashboard","DEMO\nБез автомобиля"),(s,e)=>StartActivity(new Intent(this,typeof(BavarianDemoActivity))));
            var rowInjector=TileRow();
            _injectors=Tile(rowInjector,T("Cylinder corrections · test","Коррекции цилиндров · тест"),(s,e)=>OpenVehicle("injectors"));
            AddText(T("ADVANCED","ДОПОЛНИТЕЛЬНО"),13,"#67B3FF",24);
            var row3=TileRow();
            _diagnostics=Tile(row3,T("Engine\nECU details","Двигатель\nДанные блока"),(s,e)=>OpenVehicle("engine"));
            _transmission=Tile(row3,T("Transmission\nECU details","АКПП\nДанные блока"),(s,e)=>OpenVehicle("transmission"));
            var row4=TileRow();
            _developer=Tile(row4,T("Developer\nManual PRG","Инженерный\nРежим"),(s,e)=>OpenVehicle("developer"));
            _library=Tile(row4,T("ECU library\nOffline","Библиотека\nECU"),(s,e)=>StartActivity(new Intent(this,typeof(BavarianEcuActivity))));
            AddButton(T("Language / Язык","Язык / Language"),(s,e)=>{
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("Language / Язык")
                    .SetSingleChoiceItems(new[]{"English","Русский"},_ru?1:0,(dialog,args)=>{
                        BavarianLanguage.SetRussian(this,args.Which==1);
                        (dialog as Android.Content.IDialogInterface)?.Dismiss();Recreate();
                    }).Show();
            });
            _library.Enabled = _diagnostics.Enabled = _transmission.Enabled = _developer.Enabled = _faults.Enabled = _liveEngine.Enabled = _liveEgs.Enabled = _injectors.Enabled = false;
            _ = LoadLibraryAsync();
            AddText(T("Experimental read-only vehicle diagnostics. Test while parked; verify the ECU variant first.", "Экспериментальная диагностика только для чтения. Проверяйте на стоящем автомобиле; сначала подтвердите вариант ECU."), 14, "#C2CDDD", 24);
            AddText("Based on Deep OBD / EdiabasLib by Ulrich Holeschak · GPL-3.0", 12, "#C2CDDD", 16);
        }

        private LinearLayout TileRow()
        {
            var row=new LinearLayout(this){Orientation=Orientation.Horizontal};
            _body.AddView(row,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(8)});
            return row;
        }
        private Button Tile(LinearLayout row,string label,EventHandler click)
        {
            var b=new Button(this){Text=label,TextSize=15};
            b.SetAllCaps(false);b.SetTextColor(C("#F2F6FC"));
            var shape=new Android.Graphics.Drawables.GradientDrawable();
            shape.SetColor(C("#19283B"));shape.SetCornerRadius(Dp(12));
            b.Background=shape;b.SetPadding(Dp(8),Dp(12),Dp(8),Dp(12));b.Click+=click;
            var lp=new LinearLayout.LayoutParams(0,Dp(104),1);lp.SetMargins(Dp(4),0,Dp(4),0);row.AddView(b,lp);return b;
        }
        private void OpenVehicle(string mode)
        {
            var intent = new Intent(this, typeof(BavarianVehicleActivity));
            intent.PutExtra("bavarian_mode", mode);
            StartActivity(intent);
        }

        protected override void OnResume()
        {
            base.OnResume();
            BavarianLanguage.EnsureEngineEnglish();
            RefreshStatus();
        }

        private TextView AddText(string text, int sp, string color, int top = 6)
        {
            var view = new TextView(this) { Text = text, TextSize = sp };
            view.SetTextColor(C(color));
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = Dp(top);
            _body.AddView(view, lp);
            return view;
        }

        private Button AddButton(string label, EventHandler handler)
        {
            var button = new Button(this) { Text = label };
            button.Click += handler;
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = Dp(12);
            _body.AddView(button, lp);
            return button;
        }

        private async Task LoadLibraryAsync()
        {
            _ecu.Text = "Checking local ECU library...";
            string report = await Task.Run(() => BavarianEcuSeed.Initialize(ApplicationContext));
            if (IsFinishing || IsDestroyed) return;
            _ecu.Text = report;
            _library.Enabled = true;
            _diagnostics.Enabled = _transmission.Enabled = _developer.Enabled = _faults.Enabled = _liveEngine.Enabled = _liveEgs.Enabled = _injectors.Enabled = report.StartsWith("E60 V74 ECU library: 675 / 675 verified", StringComparison.Ordinal);
        }

        private void RefreshStatus()
        {
            if (_usb == null) return;
            try
            {
                var manager = GetSystemService(UsbService) as UsbManager;
                var devices = manager?.DeviceList?.Values;
                if (devices == null || devices.Count == 0)
                    _usb.Text = "No USB device detected. Connect the OTG cable.";
                else
                    _usb.Text = string.Join("\n", devices.Select(d =>
                        $"{d.VendorId:X4}:{d.ProductId:X4}  " +
                        (d.VendorId == 0x0403 && (d.ProductId == 0x6001 || d.ProductId == 0x6015 || d.ProductId == 0)
                            ? T("FTDI candidate", "Возможный FTDI-адаптер") : T("USB device", "USB-устройство"))));
            }
            catch (Exception)
            {
                _usb.Text = "USB enumeration failed.";
            }

        }
    }
}
#endif
