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
        private Button _diagnostics, _library;
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
            AddText("DIAGNOSTICS  /  0.9 PREVIEW", 15, "#67B3FF");
            AddText("BMW E60 · M57 · K+DCAN USB", 16, "#C2CDDD");
            AddButton(T("Language: English / Русский", "Язык: Русский / English"), (s,e) =>
            {
                new AndroidX.AppCompat.App.AlertDialog.Builder(this)
                    .SetTitle("Language / Язык")
                    .SetSingleChoiceItems(new[] { "English", "Русский" }, _ru ? 1 : 0, (dialog, args) =>
                    {
                        BavarianLanguage.SetRussian(this, args.Which == 1);
                        (dialog as Android.Content.IDialogInterface)?.Dismiss();
                        Recreate();
                    }).Show();
            });
            AddText("Diagnostic errors: English", 13, "#C2CDDD");
            AddText(T("USB ADAPTER", "USB-АДАПТЕР"), 14, "#67B3FF", 24);
            _usb = AddText("", 16, "#F2F6FC");
            AddText(T("DIAGNOSTIC DATA", "ДИАГНОСТИЧЕСКИЕ ДАННЫЕ"), 14, "#67B3FF", 24);
            _ecu = AddText("", 16, "#F2F6FC");
            AddButton(T("Refresh hardware check", "Обновить проверку USB"), (s,e) => RefreshStatus());
            _library = AddButton(T("Import / inspect local ECU files", "Импорт и просмотр файлов ECU"), (s,e) =>
                StartActivity(new Intent(this, typeof(BavarianEcuActivity))));
            _diagnostics = AddButton(T("Open diagnostic engine", "Открыть диагностику"), (s,e) =>
            {
                StartActivity(new Intent(this, typeof(BavarianVehicleActivity)));
            });
            _library.Enabled = _diagnostics.Enabled = false;
            _ = LoadLibraryAsync();
            AddText(T("Experimental read-only vehicle diagnostics. Test while parked; verify the ECU variant first.", "Экспериментальная диагностика только для чтения. Проверяйте на стоящем автомобиле; сначала подтвердите вариант ECU."), 14, "#C2CDDD", 24);
            AddText("Based on Deep OBD / EdiabasLib by Ulrich Holeschak · GPL-3.0", 12, "#C2CDDD", 16);
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
            _library.Enabled = _diagnostics.Enabled = true;
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
