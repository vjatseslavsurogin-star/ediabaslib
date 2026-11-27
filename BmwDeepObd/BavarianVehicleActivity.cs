#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using EdiabasLib;

namespace BmwDeepObd
{
    [BroadcastReceiver(Exported = false)]
    public class BavarianUsbPermissionReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent) { }
    }

    [Activity(Label = "Vehicle diagnostics", Exported = false,
        Name = ActivityCommon.AppNameSpace + "." + nameof(BavarianVehicleActivity))]
    public class BavarianVehicleActivity : AppCompatActivity
    {
        private static readonly SemaphoreSlim TransportLock = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string,string> Approved = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["d50m57c0.prg"]="9da5becea95792b7befba23aa4731bc8cb0ef9ae4f857a5565af595e2a0ecbfe",
            ["d50m57d0.prg"]="75751edc17d26199a2300015fc93d2f91d3ad6bd9c208a6595f0e274b615adc4",
            ["d50m57e0.prg"]="7d3964204c5f6c9f6ba630585d7754ea57b5c5a5adc1cb8d3177a0b2f19370a4",
            ["d60m57a0.prg"]="5e9ac1f8ff66975d22d74c16a5d3904f3b6177c5610f48ad3cac0e33400fd58c",
            ["d60m57d0.prg"]="05039bb238c7ebc4bd8acbda18530e8f4936537cafbec86ea8d1a49c179fbfe0",
            ["d62m57a0.prg"]="2c8597349da72e11780d6c1ec2029fab152ac55640c11fa0379de7cbce95d082",
            ["d62m57b0.prg"]="5470bf3de295eca45d903fd1434ec4a1c2714aa300d4b1dce6c19884fc65af32",
            ["gs19.prg"]="48b54e3e6505f53e28e4ac827a420e630eea620dd403488afe1c027ebd6cb235",
            ["gs19a.prg"]="a4e8e1280a46db684607a5456f8831683eb7ef563236b544ca388c44dd58c266",
            ["gs19b.prg"]="a7fd76d1ca9a7c8947df65d03ee038e995af5f3afc9ab0aa321e37f8f11bb0bd",
            ["gs19d.prg"]="a49b6a6f0d036faaa47a6cc34dcb9d1f3b71affa62f37228dfd841cf28ec5b1e"
        };
        private static readonly string[] EngineJobs =
        {
            "STATUS_MOTORDREHZAHL","STATUS_KUEHLMITTELTEMPERATUR","STATUS_LADEDRUCK_IST",
            "STATUS_LADEDRUCK_SOLL","STATUS_RAILDRUCK_IST","STATUS_RAILDRUCK_SOLL","STATUS_UBATT"
        };
        private static readonly string[] GearboxJobs = { "STATUS_GETRIEBETEMPERATUR", "STATUS_ISTGANG" };
        private LinearLayout _body;
        private Spinner _variant;
        private TextView _state, _output;
        private CheckBox _confirm;
        private string _fingerprint;
        private readonly Handler _handler = new Handler(Looper.MainLooper);
        private Java.Lang.Runnable _poll;
        private string _activeDevice;
        private bool _visible;
        private int _ok, _failed;
        private TextView _stats;
        private string LogPath => Path.Combine(FilesDir.AbsolutePath,"bavarian-test.log");
        private Button _permission, _ident, _faults, _snapshot;
        private string _identified, _deviceName;
        private bool _busy, _ru;
        private CancellationTokenSource _cancel;
        private string Root => Path.Combine(FilesDir.AbsolutePath,"local-ecu");
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _ru=BavarianLanguage.IsRussian(this);
            SupportActionBar?.Hide();
            var scroll=new ScrollView(this){FillViewport=true};
            scroll.SetFitsSystemWindows(true);
            _body=new LinearLayout(this){Orientation=Orientation.Vertical};
            _body.SetPadding(Dp(20),Dp(20),Dp(20),Dp(20));
            scroll.AddView(_body);
            SetContentView(scroll);
            AddText(T("Vehicle diagnostics · USB","Диагностика автомобиля · USB"),24);
            AddText("READ ONLY / EXPERIMENTAL",14);
            AddText(T("Park safely, ignition on. Select a known ECU variant. Connect only one K+DCAN cable.","Автомобиль должен стоять, зажигание включено. Выберите известный вариант ECU. Подключите один K+DCAN."),15);
            _state=AddText("",15);
            _permission=AddButton(T("Check USB / grant permission","Проверить USB / разрешение"),(s,e)=>RequestUsb());
            _variant=new Spinner(this);
            var variants=Approved.Keys.Where(n=>File.Exists(Path.Combine(Root,n))).ToArray();
            var adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerItem,variants);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _variant.Adapter=adapter;
            _body.AddView(_variant);
            _ident=AddButton(T("Identify ECU (IDENT)","Идентификация ECU"),async(s,e)=>await RunAsync("IDENT"));
            _confirm=new CheckBox(this){Text=T("I checked the identification and selected ECU variant","Я проверил идентификацию и выбранный вариант ECU"),Enabled=false};
            _body.AddView(_confirm);
            _confirm.CheckedChange+=(s,e)=>Refresh();
            _faults=AddButton(T("Read fault memory","Чтение ошибок"),async(s,e)=>await RunAsync("FS_LESEN"));
            _snapshot=AddButton(T("Read parameters once","Прочитать параметры"),async(s,e)=>await RunAsync("SNAPSHOT"));
            AddButton(T("Cancel / disconnect","Отмена / отключение"),(s,e)=>{ResetIdentity();_cancel?.Cancel();Refresh();});
            _stats=AddText("",13);
            AddButton(T("Share developer report","Поделиться инженерным отчётом"),(s,e)=>ShareReport());
            AddButton(T("Clear local test report","Очистить локальный отчёт"),(s,e)=>{
                if(_busy)return;
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetMessage("Delete local diagnostic report?").SetPositiveButton("Delete",(d,a)=>{try{File.Delete(LogPath);_ok=_failed=0;Refresh();}catch(Exception ex){_output.Text=BavarianLanguage.Error("Report deletion failed",ex);}}).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            });
            _output=AddText("",14);
            _poll=new Java.Lang.Runnable(()=>{if(!_visible)return;Refresh();_handler.PostDelayed(_poll,1000);});
            _variant.ItemSelected+=(s,e)=>{ResetIdentity();Refresh();};
            Refresh();
        }

        protected override void OnResume(){base.OnResume();_visible=true;if(_state!=null)Refresh();if(_poll!=null)_handler.PostDelayed(_poll,1000);}
        protected override void OnStop(){_visible=false;if(_poll!=null)_handler.RemoveCallbacks(_poll);_cancel?.Cancel();ResetIdentity();base.OnStop();}
        private void ResetIdentity(){_identified=null;_deviceName=null;_fingerprint=null;if(_confirm!=null&&_confirm.Checked)_confirm.Checked=false;}
        private static readonly object LogLock=new object();
        private void Log(string message)
        {
            lock(LogLock)
            {
                try
                {
                    if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>512*1024)File.Delete(LogPath);
                    File.AppendAllText(LogPath,DateTimeOffset.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" "+message+"\n",Encoding.UTF8);
                }
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
        private void ShareReport()
        {
            try
            {
                if(_busy){_output.Text="Finish or cancel the request before sharing.";return;}
                string report;
                lock(LogLock){report=File.Exists(LogPath)?File.ReadAllText(LogPath):"No diagnostic requests recorded.";}
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("Share diagnostic report")
                    .SetMessage("The report may contain vehicle identifiers and ECU responses. Share only with a trusted recipient.")
                    .SetPositiveButton("Share",(d,a)=>{
                        var send=new Intent(Intent.ActionSend);send.SetType("text/plain");
                        send.PutExtra(Intent.ExtraSubject,"Bavarian V1.0 diagnostic report");
                        send.PutExtra(Intent.ExtraText,report);
                        StartActivity(Intent.CreateChooser(send,"Share report"));
                    }).SetNegativeButton("Cancel",(d,a)=>{}).Show();
            }
            catch(Exception ex){_output.Text=BavarianLanguage.Error("Report sharing failed",ex);}
        }
        private TextView AddText(string text,int size)
        {
            var v=new TextView(this){Text=text,TextSize=size};
            v.SetTextIsSelectable(true);
            var lp=new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(12)};
            _body.AddView(v,lp);return v;
        }
        private Button AddButton(string label,EventHandler click)
        {
            var b=new Button(this){Text=label};b.Click+=click;_body.AddView(b);return b;
        }
        private UsbManager Manager=>GetSystemService(UsbService) as UsbManager;
        private UsbDevice GetDevice(UsbManager manager)
        {
            if(manager==null)return null;
            var drivers=EdFtdiInterface.GetDriverList(manager);
            return drivers.Count==1 ? drivers[0].Device : null;
        }
        private string Selected=>_variant?.SelectedItem?.ToString();
        private void Refresh()
        {
            bool permitted=false;
            try
            {
                var manager=Manager; var device=GetDevice(manager);
                permitted=device!=null && manager.HasPermission(device);
                if(device==null || device.DeviceName!=_deviceName || !permitted){ResetIdentity();}
                if(_busy&&(!permitted||device?.DeviceName!=_activeDevice))_cancel?.Cancel();
                _state.Text=_busy?"Diagnostic request running...":device==null?"Connect exactly one supported USB adapter.":!permitted?"USB detected. Permission required.":_identified==null?"USB permission granted. ECU not identified.":"ECU responded to IDENT. Confirm variant before interpreting results.";
            }
            catch(Exception){_state.Text="USB check failed.";ResetIdentity();_cancel?.Cancel();}
            _permission.Enabled=!_busy;
            _variant.Enabled=!_busy;
            _ident.Enabled=!_busy&&permitted&&Selected!=null;
            bool identified=permitted&&Selected!=null&&Selected==_identified;
            _confirm.Enabled=!_busy&&identified;
            bool ready=!_busy&&identified&&_confirm.Checked;
            _faults.Enabled=ready;_snapshot.Enabled=ready;
            _stats.Text="Jobs: "+_ok+" OK / "+_failed+" failed (this screen)";
        }
        private void RequestUsb()
        {
            try
            {
                var manager=Manager;var device=GetDevice(manager);
                if(device==null){Refresh();return;}
                if(!manager.HasPermission(device))
                {
                    var intent=new Intent(this,typeof(BavarianUsbPermissionReceiver));
                    intent.SetAction(ActivityCommon.AppNameSpace+".BAVARIAN_USB");
                    var flags=OperatingSystem.IsAndroidVersionAtLeast(31)?PendingIntentFlags.Mutable:0;
                    var pending=PendingIntent.GetBroadcast(this,510,intent,flags);
                    manager.RequestPermission(device,pending);
                    _output.Text="Grant USB permission, then press Check USB again.";
                }
                Refresh();
            }
            catch(Exception ex){_output.Text=BavarianLanguage.Error("USB permission request failed",ex);}
        }
        private async Task RunAsync(string operation)
        {
            if(_busy)return;
            string name=Selected;
            if(name==null || !Approved.ContainsKey(name))return;
            UsbManager manager; UsbDevice device;
            try { manager=Manager; device=GetDevice(manager); }
            catch(Exception ex) { _output.Text=BavarianLanguage.Error("USB check failed",ex); return; }
            if(device==null || !manager.HasPermission(device)){Refresh();return;}
            if(operation!="IDENT" && (_identified!=name || _deviceName!=device.DeviceName || !_confirm.Checked || _fingerprint==null))return;
            if(operation=="IDENT")ResetIdentity();
            string expected=_fingerprint;
            _activeDevice=device.DeviceName;
            Log("START "+operation+" PRG="+name+" USB="+device.VendorId.ToString("X4")+":"+device.ProductId.ToString("X4"));
            _busy=true;_cancel=new CancellationTokenSource();
            _cancel.CancelAfter(TimeSpan.FromSeconds(45));
            var token=_cancel.Token;
            Refresh();_output.Text="Waiting for ECU response...";
            try
            {
                string[] jobs=operation=="SNAPSHOT"?(name.StartsWith("gs19",StringComparison.Ordinal)?GearboxJobs:EngineJobs):new[]{operation};
                var result=await Task.Run(()=>Execute(manager,device.DeviceName,name,jobs,expected,token));
                if(IsFinishing||IsDestroyed||token.IsCancellationRequested)return;
                _output.Text=result.Text;
                _ok+=result.Ok;_failed+=result.Failed;
                if(operation=="IDENT"){_identified=name;_deviceName=device.DeviceName;_fingerprint=result.Identity;}
            }
            catch(Exception ex)
            {
                ResetIdentity();_failed++;
                string failure=token.IsCancellationRequested?"Diagnostic request cancelled or timed out.":Describe(ex);
                Log("FAIL "+failure);
                if(!IsFinishing&&!IsDestroyed)_output.Text=failure;
            }
            finally
            {
                _cancel.Dispose();_cancel=null;_busy=false;_activeDevice=null;
                if(!IsFinishing&&!IsDestroyed)Refresh();
            }
        }
        private sealed class RequestResult
        {
            public string Text,Identity;
            public int Ok,Failed;
        }
        private static string Describe(Exception ex)
        {
            if(ex is EdiabasNet.EdiabasNetException ee)return "EDIABAS "+ee.ErrorCode+" — "+EdiabasNet.GetErrorDescription(ee.ErrorCode);
            if(ex is IOException || ex is InvalidOperationException)return ex.GetType().Name+": "+ex.Message;
            return BavarianLanguage.Error("Diagnostic request failed",ex);
        }
        private RequestResult Execute(UsbManager manager,string deviceName,string name,string[] jobs,string expected,CancellationToken token)
        {
            if(!TransportLock.Wait(0))throw new InvalidOperationException("Transport busy");
            try
            {
                token.ThrowIfCancellationRequested();
                var current=GetDevice(manager);
                if(current==null||current.DeviceName!=deviceName||!manager.HasPermission(current))throw new IOException("USB changed");
                using(var stream=File.OpenRead(Path.Combine(Root,name)))
                {
                    string hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if(hash!=Approved[name])throw new IOException("ECU file hash mismatch");
                }
                using var ediabas=new EdiabasNet();
                ediabas.SetConfigProperty("EcuPath",Root);
                ediabas.SgbdFileName=name;
                ediabas.AbortJobFunc=()=>token.IsCancellationRequested;
                var transport=new EdInterfaceObd();
                ediabas.EdInterfaceClass=transport;
                transport.ComPort=EdFtdiInterface.PortId+"0";
                transport.ConnectParameter=new EdFtdiInterface.ConnectParameterType(manager);
                using var registration=token.Register(()=>transport.TransmitCancel(true));
                var lines=new List<string>();
                var result=new RequestResult();
                if(expected!=null)jobs=new[]{"IDENT"}.Concat(jobs).ToArray();
                foreach(string job in jobs)
                {
                    token.ThrowIfCancellationRequested();
                    if(job!="IDENT"&&job!="FS_LESEN"&&!EngineJobs.Contains(job)&&!GearboxJobs.Contains(job))throw new InvalidOperationException("Job blocked");
                    var sw=Stopwatch.StartNew();
                    var jobLines=new List<string>();
                    try
                    {
                    ediabas.ArgString=string.Empty;
                    ediabas.ArgBinaryStd=null;
                    ediabas.ResultsRequests=string.Empty;
                    ediabas.ExecuteJob(job);
                    var rows=ediabas.ResultSets;
                    if(rows==null||rows.Count<2)throw new IOException("No ECU results");
                    var data=rows.Skip(1).ToArray();
                    var statuses=data.Where(r=>r.ContainsKey("JOB_STATUS")).Select(r=>Convert.ToString(r["JOB_STATUS"].OpData,CultureInfo.InvariantCulture)).ToArray();
                    if(statuses.Any(s=>!string.Equals(s,"OKAY",StringComparison.OrdinalIgnoreCase)))throw new IOException("ECU reported unsuccessful job");
                    if(job=="IDENT"&&!data.Any(r=>r.Keys.Any(k=>k!="JOB_STATUS")))throw new IOException("No identification data");
                    jobLines.Add(job);
                    foreach(var row in data)
                        foreach(var kv in row)
                        {
                            if(kv.Key=="JOB_STATUS")continue;
                            string value=kv.Value.OpData is byte[] bytes?Convert.ToHexString(bytes):Convert.ToString(kv.Value.OpData,CultureInfo.InvariantCulture);
                            jobLines.Add(kv.Key+": "+value);
                        }
                    if(job=="IDENT")
                    {
                        result.Identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",jobLines.Skip(1).OrderBy(x=>x,StringComparer.Ordinal)))));
                        if(expected!=null&&expected!=result.Identity)throw new IOException("ECU identification changed; re-identify manually.");
                    }
                    token.ThrowIfCancellationRequested();
                    string report=string.Join("\n",jobLines);
                    lines.Add(report+"\nResponse: "+sw.ElapsedMilliseconds+" ms\n");
                    Log("OK "+job+" "+sw.ElapsedMilliseconds+" ms\n"+report);
                    result.Ok++;
                    }
                    catch(Exception ex)
                    {
                        Log("FAIL "+job+" "+sw.ElapsedMilliseconds+" ms "+Describe(ex));
                        if(job=="IDENT"||jobs.Length<=2||token.IsCancellationRequested)throw;
                        // Preserve successful readings; stop on transport errors.
                        if(ex is EdiabasNet.EdiabasNetException || ex is IOException)throw;
                        result.Failed++;
                        lines.Add(job+": "+Describe(ex));
                    }
                }
                result.Text=string.Join("\n",lines);
                return result;
            }
            finally{TransportLock.Release();}
        }
    }
}
#endif
