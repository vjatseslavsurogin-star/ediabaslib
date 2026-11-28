#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Path=System.IO.Path;
namespace BmwDeepObd
{
    [Activity(Label="Saved diagnostics",Exported=false,Name=ActivityCommon.AppNameSpace+"."+nameof(BavarianOfflineActivity))]
    public class BavarianOfflineActivity:AppCompatActivity
    {
        private bool _ru,_reports,_loading;
        private LinearLayout _body,_content;
        private TextView _state;
        private string _report;
        private Sample[] _first=Array.Empty<Sample>(),_second=Array.Empty<Sample>();
        private string _a="",_b="";
        private sealed record Sample(DateTimeOffset Time,string Variant,string Job,double Value,string Unit);
        private string T(string en,string ru)=>_ru?ru:en;
        protected override void OnCreate(Bundle saved)
        {
            BavarianUi.ApplyTheme(this);base.OnCreate(saved);SupportActionBar?.Hide();_ru=BavarianLanguage.IsRussian(this);_reports=Intent?.GetBooleanExtra("reports",false)??false;
            BavarianUi.Shell(this,_reports?1:2,_ru,i=>BavarianUi.NavigateHome(this,i),out _body);
            _body.AddView(BavarianUi.Text(this,_reports?T("Reports & comparison","Отчёты и сравнение"):T("Log viewer","Просмотр логов"),28,true));
            _body.AddView(BavarianUi.Text(this,_reports?T("Local saved reports. May contain identifiers. Export only to a trusted location.","Локальные отчёты. Могут содержать идентификаторы. Сохраняйте в доверенное место."):T("ECU CSV only. Compare the same variant, parameter and unit over elapsed time; driving conditions may differ.","Только ECU CSV. Сравнение одного варианта, параметра и единицы по времени; условия поездок могут отличаться."),13));
            if(_reports)
            {
                BavarianUi.Action(_body,T("Choose report","Выбрать отчёт"),(s,e)=>ChooseReport(),true);
                BavarianUi.Action(_body,T("Compare two scans","Сравнить две диагностики"),(s,e)=>ChooseComparison());
            }
            else
            {
                BavarianUi.Action(_body,T("Choose session A","Выбрать сессию A"),(s,e)=>ChooseLog(false),true);
                BavarianUi.Action(_body,T("Choose session B · optional","Выбрать сессию B · необязательно"),(s,e)=>ChooseLog(true));
                BavarianUi.Action(_body,T("Clear comparison","Очистить сравнение"),(s,e)=>{if(_loading)return;_second=Array.Empty<Sample>();_b="";RenderSignals();});
            }
            _state=BavarianUi.Text(this,"",13);_body.AddView(_state);
            _content=new LinearLayout(this){Orientation=Orientation.Vertical};_body.AddView(_content);
        }
        private void ChooseLog(bool second)
        {
            if(_loading)return;
            try
            {
                string dir=Path.Combine(FilesDir.AbsolutePath,"bavarian-csv");
                var files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.csv").OrderByDescending(x=>x,StringComparer.Ordinal).ToArray():Array.Empty<string>();
                if(files.Length==0){_state.Text=T("No saved logs.","Нет сохранённых логов.");return;}
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetItems(files.Select(Path.GetFileName).ToArray(),async(d,a)=>{
                    if(_loading)return;_loading=true;string file=files[a.Which];_state.Text=T("Loading CSV…","Чтение CSV…");
                    try
                    {
                        var samples=await Task.Run(()=>ReadCsv(file));if(IsFinishing||IsDestroyed)return;if(samples.Length==0)throw new IOException("No valid ECU samples");
                        if(second){_second=samples;_b=Path.GetFileName(files[a.Which]);}
                        else{_first=samples;_a=Path.GetFileName(files[a.Which]);}
                        RenderSignals();
                    }catch(Exception ex){if(!IsFinishing&&!IsDestroyed)_state.Text=ex.Message;}finally{_loading=false;}
                }).Show();
            }catch(Exception ex){_state.Text=ex.Message;}
        }
        private static string[] CsvFields(string line)
        {
            var fields=new List<string>();var b=new StringBuilder();bool quoted=false;
            for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){b.Append('"');i++;}else quoted=!quoted;}else if(c==','&&!quoted){fields.Add(b.ToString());b.Clear();}else b.Append(c);}
            if(quoted)return Array.Empty<string>();fields.Add(b.ToString());return fields.ToArray();
        }
        private static Sample[] ReadCsv(string path)
        {
            if(new FileInfo(path).Length>20*1024*1024)throw new IOException("Log exceeds 20 MB viewer limit");
            using var r=new StreamReader(path);if(r.ReadLine()!=BavarianLiveFrame.CsvHeader)throw new IOException("Unknown CSV format");
            var list=new List<Sample>();string line;
            while((line=r.ReadLine())!=null)
            {
                if(list.Count>=100000)throw new IOException("Log exceeds 100000 sample viewer limit");
                var f=CsvFields(line);
                if(f.Length!=8||f[1]!="ECU"||!DateTimeOffset.TryParse(f[0],CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)||!double.TryParse(f[4],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))continue;
                list.Add(new Sample(time,f[2],f[3],value,f[5]));
            }
            return list.ToArray();
        }
        private void RenderSignals()
        {
            _content.RemoveAllViews();_state.Text="A: "+(_a==""?"—":_a)+"\nB: "+(_b==""?"—":_b);
            if(_first.Length==0)return;
            var secondKeys=_second.Select(x=>(x.Variant,x.Job,x.Unit)).ToHashSet();
            var keys=_first.Select(x=>(x.Variant,x.Job,x.Unit)).Distinct().Where(k=>_second.Length==0||secondKeys.Contains(k)).ToArray();
            if(keys.Length==0){_content.AddView(BavarianUi.Text(this,T("No comparable parameters.","Нет сопоставимых параметров."),15,true));return;}
            foreach(var k in keys)
            {
                var key=k;
                BavarianUi.Action(_content,BavarianLiveFrame.Label(key.Job,_ru)+" · "+key.Unit,(s,e)=>{
                    var first=_first.Where(x=>x.Variant==key.Variant&&x.Job==key.Job&&x.Unit==key.Unit).OrderBy(x=>x.Time).ToArray();
                    var second=_second.Where(x=>x.Variant==key.Variant&&x.Job==key.Job&&x.Unit==key.Unit).OrderBy(x=>x.Time).ToArray();
                    var panel=new LinearLayout(this){Orientation=Orientation.Vertical};panel.AddView(new LogPlot(this,first,second),new LinearLayout.LayoutParams(-1,BavarianUi.Dp(this,240)));
                    string Summary(string label,Sample[] data)=>data.Length==0?"":label+" · n="+data.Length+"\nmin "+data.Min(x=>x.Value).ToString("0.##",CultureInfo.InvariantCulture)+"  max "+data.Max(x=>x.Value).ToString("0.##",CultureInfo.InvariantCulture)+"  mean "+data.Average(x=>x.Value).ToString("0.##",CultureInfo.InvariantCulture)+" "+key.Unit;
                    panel.AddView(BavarianUi.Text(this,Summary("A",first)+"\n"+Summary("B",second),14,true));
                    panel.AddView(BavarianUi.Text(this,T("Descriptive statistics only; not a tuning or health verdict.","Только описательная статистика, не заключение о тюнинге или исправности."),12));
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(BavarianLiveFrame.Label(key.Job,_ru)).SetView(panel).SetPositiveButton("OK",(d,a)=>{}).Show();
                });
            }
        }
        private sealed class LogPlot:View
        {
            private readonly Sample[] _a,_b;private readonly Paint _p=new Paint(PaintFlags.AntiAlias);
            private readonly double _min,_max;
            public LogPlot(Context c,Sample[] a,Sample[] b):base(c){_a=a;_b=b;var all=a.Concat(b);_min=all.Select(s=>s.Value).DefaultIfEmpty(0).Min();_max=all.Select(s=>s.Value).DefaultIfEmpty(1).Max();}
            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);canvas.DrawColor(BavarianUi.Surface);
                if(_a.Length==0&&_b.Length==0)return;
                double min=_min,max=_max;if(max-min<1e-8){min-=0.5;max+=0.5;}
                double Seconds(Sample[] s)=>s.Length==0?0:(s[s.Length-1].Time-s[0].Time).TotalSeconds;
                double duration=Math.Max(1,Math.Max(Seconds(_a),Seconds(_b)));
                float left=12,top=30,right=Width-12,bottom=Height-30;if(right<=left||bottom<=top)return;
                _p.Color=BavarianUi.Muted;_p.TextSize=12*Resources.DisplayMetrics.Density;canvas.DrawText(max.ToString("0.##",CultureInfo.InvariantCulture),left,top,_p);canvas.DrawText(min.ToString("0.##",CultureInfo.InvariantCulture),left,bottom,_p);canvas.DrawText(duration.ToString("0.0",CultureInfo.InvariantCulture)+" s",left,Height-5,_p);
                void Draw(Sample[] series,Color color)
                {
                    if(series.Length==0)return;_p.Color=color;_p.StrokeWidth=2*Resources.DisplayMetrics.Density;int step=Math.Max(1,series.Length/2000);float X(Sample s)=>left+(right-left)*(float)((s.Time-series[0].Time).TotalSeconds/duration);float Y(Sample s)=>bottom-(bottom-top)*(float)((s.Value-min)/(max-min));
                    var last=series[0];canvas.DrawCircle(X(last),Y(last),3,_p);
                    for(int i=step;i<series.Length;i+=step){var now=series[i];canvas.DrawLine(X(last),Y(last),X(now),Y(now),_p);last=now;}
                    var end=series[series.Length-1];canvas.DrawLine(X(last),Y(last),X(end),Y(end),_p);
                }
                Draw(_a,BavarianUi.Blue);Draw(_b,BavarianUi.Compare);
            }
        }
        private void ChooseComparison()
        {
            if(_loading)return;
            try
            {
                var dir=Path.Combine(FilesDir.AbsolutePath,"bavarian-history");
                var files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.scan.json").OrderBy(x=>x,StringComparer.Ordinal).ToArray():Array.Empty<string>();
                BavarianScanSnapshot Read(string file)
                {
                    if(new FileInfo(file).Length>512*1024)throw new IOException("Snapshot too large");
                    return System.Text.Json.JsonSerializer.Deserialize<BavarianScanSnapshot>(File.ReadAllText(file));
                }
                var scans=new List<BavarianScanSnapshot>();
                foreach(var file in files)try{var x=Read(file);if(x?.Schema==1&&BavarianScanSnapshot.ValidVin(x.Vin))scans.Add(x);}catch(Exception){}
                if(scans.Count<2){_state.Text=T("Two new scans with a verified VIN are needed. Include CAS in the scan.","Нужны две новые проверки с подтверждённым VIN. Включите CAS в сканирование.");return;}
                string Label(BavarianScanSnapshot x)=>x.Time+" · "+x.Vin+(x.Partial?T(" · partial"," · частично"):"")+(x.Imported?T(" · restored"," · восстановлено"):"");
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Choose earlier scan","Выберите раннюю проверку")).SetItems(scans.Select(Label).ToArray(),(d,a)=>{
                    var first=scans[a.Which];var later=scans.Skip(a.Which+1).Where(x=>x.Vin==first.Vin).ToArray();
                    if(later.Length==0){_state.Text=T("No later scan with the same verified VIN.","Нет поздней проверки с тем же подтверждённым VIN.");return;}
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle(T("Choose later scan","Выберите позднюю проверку")).SetItems(later.Select(Label).ToArray(),(dd,aa)=>{
                        try{_report=string.Join("\n",BavarianScanSnapshot.Compare(first,later[aa.Which],_ru));RenderReport();}catch(Exception ex){_state.Text=ex.Message;}
                    }).Show();
                }).Show();
            }catch(Exception ex){_state.Text=ex.Message;}
        }
        private void RenderReport()
        {
            _content.RemoveAllViews();_content.AddView(BavarianUi.Text(this,_report,13));
            BavarianUi.Action(_content,T("Save PDF","Сохранить PDF"),(s,e)=>{var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("application/pdf");intent.PutExtra(Intent.ExtraTitle,"Bavarian-report.pdf");StartActivityForResult(intent,725);},true);
        }
        private void ChooseReport()
        {
            try
            {
                string dir=Path.Combine(FilesDir.AbsolutePath,"bavarian-history");var files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.txt").OrderByDescending(x=>x,StringComparer.Ordinal).ToArray():Array.Empty<string>();
                if(files.Length==0){_state.Text=T("No saved reports.","Нет сохранённых отчётов.");return;}
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetItems(files.Select(file=>{try{return string.Join(" · ",File.ReadLines(file).Take(2));}catch(Exception){return Path.GetFileName(file);}}).ToArray(),(d,a)=>{
                    try
                    {
                        if(new FileInfo(files[a.Which]).Length>512*1024)throw new IOException("Report exceeds PDF export limit");
                        _report=File.ReadAllText(files[a.Which]);RenderReport();
                    }catch(Exception ex){_state.Text=ex.Message;}
                }).Show();
            }catch(Exception ex){_state.Text=ex.Message;}
        }
        protected override void OnActivityResult(int requestCode,Result resultCode,Intent data)
        {
            base.OnActivityResult(requestCode,resultCode,data);
            if(requestCode!=725||resultCode!=Result.Ok||data?.Data==null||_report==null)return;
            try
            {
                using var output=ContentResolver.OpenOutputStream(data.Data);
                if(output==null)throw new IOException("Unable to open destination");
                using var pdf=new PdfDocument();using var paint=new Paint(PaintFlags.AntiAlias);paint.Color=Color.Black;paint.TextSize=10;
                var wrapped=new List<string>();
                foreach(string line in _report.Replace("\r","").Split('\n'))
                {
                    if(line.Length==0){wrapped.Add("");continue;}
                    var rest=line;
                    while(rest.Length>0){int n=Math.Min(95,rest.Length);while(n>1&&paint.MeasureText(rest.Substring(0,n))>515)n--;wrapped.Add(rest.Substring(0,n));rest=rest.Substring(n);}
                }
                int page=0;
                foreach(var chunk in wrapped.Chunk(49))
                {
                    page++;var info=new PdfDocument.PageInfo.Builder(595,842,page).Create();var p=pdf.StartPage(info);
                    paint.TextSize=18;p.Canvas.DrawText("BAVARIAN  /  DIAGNOSTICS",40,42,paint);paint.TextSize=10;float y=76;
                    foreach(var line in chunk){p.Canvas.DrawText(line,40,y,paint);y+=14;}
                    p.Canvas.DrawText((_ru?"Страница ":"Page ")+page,40,810,paint);pdf.FinishPage(p);
                }
                pdf.WriteTo(output);_state.Text=T("PDF saved.","PDF сохранён.");
            }catch(Exception ex){_state.Text=T("PDF not saved: ","PDF не сохранён: ")+ex.Message;}
        }
    }
}
#endif
