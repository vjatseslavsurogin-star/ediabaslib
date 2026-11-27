#if BAVARIAN_USB_BASELINE
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Path = System.IO.Path;

namespace BmwDeepObd
{
    internal sealed class BavarianSample
    {
        public readonly string Job, Unit;
        public readonly double Value;
        public readonly DateTimeOffset Time;
        public readonly long Latency;
        public BavarianSample(string job,double value,string unit,DateTimeOffset time,long latency)
        {Job=job;Value=value;Unit=unit;Time=time;Latency=latency;}
        private static string Quote(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        public string ToCsv(bool demo,string variant)=>string.Join(",",new[]{
            Quote(Time.ToString("o",CultureInfo.InvariantCulture)),demo?"DEMO":"ECU",Quote(variant),Quote(Job),
            Value.ToString("R",CultureInfo.InvariantCulture),Quote(Unit),demo?"":Latency.ToString(CultureInfo.InvariantCulture)});
    }

    internal sealed class BavarianLiveFrame
    {
        public const string CsvHeader="timestamp_utc,source,variant,job,value,unit,latency_ms";
        public readonly bool Demo;
        public readonly string Variant;
        public readonly List<BavarianSample> Samples=new List<BavarianSample>();
        public double ElapsedSeconds;
        public BavarianLiveFrame(bool demo,string variant){Demo=demo;Variant=variant;}
        public double? Value(string job)=>Samples.FirstOrDefault(x=>x.Job==job)?.Value;
        public static bool UnitMatches(string job,string unit)
        {
            return job switch {
                "STATUS_MOTORDREHZAHL"=>unit=="rpm",
                "STATUS_KUEHLMITTELTEMPERATUR"=>unit=="deg C",
                "STATUS_LADEDRUCK_IST" or "STATUS_LADEDRUCK_SOLL"=>unit=="hPa",
                "STATUS_RAILDRUCK_IST" or "STATUS_RAILDRUCK_SOLL"=>unit=="bar",
                "STATUS_UBATT"=>unit=="V",
                "STATUS_GETRIEBETEMPERATUR"=>unit=="Grad C",
                "STATUS_ISTGANG"=>string.IsNullOrEmpty(unit),
                _=>false};
        }
        public static string Label(string job,bool ru)
        {
            return job switch {
                "STATUS_MOTORDREHZAHL"=>ru?"Обороты двигателя":"Engine speed",
                "STATUS_KUEHLMITTELTEMPERATUR"=>ru?"Охлаждающая жидкость":"Coolant",
                "STATUS_LADEDRUCK_IST"=>ru?"Наддув · факт (абс.)":"Boost · actual (abs.)",
                "STATUS_LADEDRUCK_SOLL"=>ru?"Наддув · цель (абс.)":"Boost · target (abs.)",
                "STATUS_RAILDRUCK_IST"=>ru?"Рампа · факт":"Rail · actual",
                "STATUS_RAILDRUCK_SOLL"=>ru?"Рампа · цель":"Rail · target",
                "STATUS_UBATT"=>ru?"Напряжение DDE":"DDE voltage",
                "STATUS_GETRIEBETEMPERATUR"=>ru?"Температура АКПП":"Transmission temperature",
                "STATUS_ISTGANG"=>ru?"Передача по EGS (не селектор)":"EGS reported gear (not selector)",
                _=>job};
        }
        public static string Display(string job,double value,string unit,bool ru)
        {
            string u=unit=="deg C"||unit=="Grad C"?"°C":unit=="hPa"?"mbar":unit;
            string fmt=job=="STATUS_MOTORDREHZAHL"||job=="STATUS_ISTGANG"?"0":"0.0";
            if(job=="STATUS_UBATT")fmt="0.00";
            return Label(job,ru)+"  "+value.ToString(fmt,CultureInfo.InvariantCulture)+" "+u;
        }
        private static string Get(Dictionary<string,string> r,string k)=>r.TryGetValue(k,out string value)?value??"":"";
        public static string FormatStatus(string job,IEnumerable<Dictionary<string,string>> rows,bool ru)
        {
            string key="STAT_"+job.Substring(7);
            foreach(var r in rows)
                if(double.TryParse(Get(r,key+"_WERT"),NumberStyles.Float,CultureInfo.InvariantCulture,out double value))
                {
                    string unit=Get(r,key+"_EINH");
                    if(UnitMatches(job,unit))return Display(job,value,unit,ru);
                }
            return job+": "+(ru?"нет проверенного значения":"No validated value");
        }
        public static string FormatFaults(IEnumerable<Dictionary<string,string>> rows,bool ru)
        {
            var faults=new List<Dictionary<string,string>>();
            Dictionary<string,string> current=null;
            foreach(var r in rows)
            {
                if(r.ContainsKey("F_HEX_CODE")||r.ContainsKey("F_ORT_NR"))
                {
                    if(current!=null)faults.Add(current);
                    current=new Dictionary<string,string>(r);
                }
                else if(current!=null)foreach(var kv in r)current[kv.Key]=kv.Value;
            }
            if(current!=null)faults.Add(current);
            if(faults.Count==0)return ru?"Нет записей с распознанным кодом. Проверь исходный отчёт.":"No recognized fault records. Check the raw report.";
            var output=new List<string>();
            foreach(var f in faults.OrderBy(r=>Get(r,"F_VORHANDEN_NR")=="35"?0:Get(r,"F_VORHANDEN_NR")=="33"?1:2))
            {
                string number=Get(f,"F_ORT_NR");
                string code=int.TryParse(number,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n.ToString("X4"):Get(f,"F_HEX_CODE");
                string status=Get(f,"F_VORHANDEN_NR") switch {
                    "35"=>ru?"АКТИВНАЯ И СОХРАНЁННАЯ":"ACTIVE / STORED",
                    "33"=>ru?"СОХРАНЁННАЯ, СЕЙЧАС НЕ АКТИВНА":"STORED / NOT CURRENT",
                    "32"=>ru?"НЕ ЗАРЕГИСТРИРОВАНА":"NOT OCCURRED",
                    _=>ru?"НЕИЗВЕСТНЫЙ СТАТУС":"UNKNOWN STATUS"};
                output.Add(code+"  ·  "+status+"\n"+Get(f,"F_ORT_TEXT")+"\n"+Get(f,"F_SYMPTOM_TEXT")+"\n"+Get(f,"F_VORHANDEN_TEXT")+"\n"+Get(f,"F_READY_TEXT"));
            }
            return string.Join("\n\n",output);
        }
    }

    internal sealed class BavarianTrend : View
    {
        private readonly List<(double A,double B)> _history=new List<(double,double)>();
        private readonly Paint _paint=new Paint(PaintFlags.AntiAlias);
        private readonly string _caption;
        public BavarianTrend(Context context,string caption):base(context){_caption=caption;}
        public void ClearHistory(){_history.Clear();Invalidate();}
        public void Append(double? a,double? b)
        {
            if(!a.HasValue||!b.HasValue)return;
            _history.Add((a.Value,b.Value));if(_history.Count>60)_history.RemoveAt(0);Invalidate();
        }
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float d=Resources.DisplayMetrics.Density;
            canvas.DrawColor(Color.Rgb(22,32,49));
            _paint.Color=Color.White;_paint.TextSize=13*d;_paint.StrokeWidth=1*d;
            canvas.DrawText(_caption,10*d,18*d,_paint);
            if(_history.Count==0)return;
            double lo=_history.Min(p=>Math.Min(p.A,p.B)),hi=_history.Max(p=>Math.Max(p.A,p.B));
            if(hi-lo<1){lo-=0.5;hi+=0.5;}
            float left=8*d,right=Width-8*d,top=37*d,bottom=Height-25*d;
            if(right<=left||bottom<=top)return;
            _paint.Color=Color.Rgb(170,186,205);_paint.TextSize=10*d;
            canvas.DrawText(hi.ToString("0.0",CultureInfo.InvariantCulture),left,32*d,_paint);
            canvas.DrawText(lo.ToString("0.0",CultureInfo.InvariantCulture),left,Height-9*d,_paint);
            for(int i=1;i<_history.Count;i++)
            {
                float x1=left+(right-left)*(i-1)/Math.Max(1,_history.Count-1),x2=left+(right-left)*i/Math.Max(1,_history.Count-1);
                _paint.StrokeWidth=2*d;_paint.Color=Color.Rgb(93,177,255);
                canvas.DrawLine(x1,bottom-(float)((_history[i-1].A-lo)/(hi-lo))*(bottom-top),x2,bottom-(float)((_history[i].A-lo)/(hi-lo))*(bottom-top),_paint);
                _paint.Color=Color.Rgb(240,175,76);
                canvas.DrawLine(x1,bottom-(float)((_history[i-1].B-lo)/(hi-lo))*(bottom-top),x2,bottom-(float)((_history[i].B-lo)/(hi-lo))*(bottom-top),_paint);
            }
        }
    }

    internal sealed class BavarianLivePanel : LinearLayout
    {
        private readonly bool _ru;
        private readonly TextView _title,_values;
        private readonly BavarianTrend _boost,_rail;
        public BavarianLivePanel(Context context,bool gearbox):base(context)
        {
            _ru=BavarianLanguage.IsRussian(context);
            Orientation=Orientation.Vertical;
            int d=(int)(8*Resources.DisplayMetrics.Density);
            SetPadding(d,d,d,d);SetBackgroundColor(Color.Rgb(12,20,34));
            _title=new TextView(context){TextSize=14};_title.SetTextColor(Color.Rgb(103,179,255));AddView(_title);
            _values=new TextView(context){TextSize=18};_values.SetTextColor(Color.White);AddView(_values);
            if(!gearbox)
            {
                _boost=new BavarianTrend(context,"Boost / mbar · actual blue · target amber");
                _rail=new BavarianTrend(context,"Rail / bar · actual blue · target amber");
                int height=(int)(150*Resources.DisplayMetrics.Density);
                AddView(_boost,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,height));
                AddView(_rail,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,height));
            }
        }
        public void ResetHistory(){_values.Text="";_title.Text="Waiting for readings";_boost?.ClearHistory();_rail?.ClearHistory();}
        public void UpdateFrame(BavarianLiveFrame frame)
        {
            _title.Text=(frame.Demo?"DEMO · simulated":"ECU · "+frame.Variant)+" · "+frame.ElapsedSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" s";
            _values.Text=string.Join("\n",frame.Samples.Select(s=>BavarianLiveFrame.Display(s.Job,s.Value,s.Unit,_ru)));
            _boost?.Append(frame.Value("STATUS_LADEDRUCK_IST"),frame.Value("STATUS_LADEDRUCK_SOLL"));
            _rail?.Append(frame.Value("STATUS_RAILDRUCK_IST"),frame.Value("STATUS_RAILDRUCK_SOLL"));
        }
    }

    [Activity(Label="Demo live data",Exported=false,Name=ActivityCommon.AppNameSpace+"."+nameof(BavarianDemoActivity))]
    public class BavarianDemoActivity : AppCompatActivity
    {
        private BavarianLivePanel _panel;
        private Handler _handler;
        private Java.Lang.Runnable _tick;
        private int _n;
        private bool _running;
        private StreamWriter _csv;
        private string _file;
        private TextView _status;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SupportActionBar?.Hide();
            var scroll=new ScrollView(this){FillViewport=true};scroll.SetFitsSystemWindows(true);
            var body=new LinearLayout(this){Orientation=Orientation.Vertical};
            body.SetPadding(20,20,20,20);scroll.AddView(body);SetContentView(scroll);
            body.AddView(new TextView(this){Text="DEMO · SIMULATED DATA · NO VEHICLE CONNECTION",TextSize=18});
            var start=new Button(this){Text="Start demo + CSV"};body.AddView(start);start.Click+=(s,e)=>StartDemo();
            var stop=new Button(this){Text="Stop demo"};body.AddView(stop);stop.Click+=(s,e)=>StopDemo();
            var export=new Button(this){Text="Export demo CSV"};body.AddView(export);export.Click+=(s,e)=>{
                StopDemo();
                if(_file==null||!File.Exists(_file))return;
                var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("text/csv");
                intent.PutExtra(Intent.ExtraTitle,"bavarian-DEMO.csv");StartActivityForResult(intent,612);
            };
            _status=new TextView(this);body.AddView(_status);
            _panel=new BavarianLivePanel(this,false);body.AddView(_panel);
            _handler=new Handler(Looper.MainLooper);_tick=new Java.Lang.Runnable(Tick);
        }
        private void StartDemo()
        {
            if(_running)return;
            try
            {
                _file=Path.Combine(FilesDir.AbsolutePath,"bavarian-DEMO.csv");
                _csv=new StreamWriter(_file,false,new UTF8Encoding(false));_csv.WriteLine(BavarianLiveFrame.CsvHeader);
                _n=0;_panel.ResetHistory();_running=true;_status.Text="DEMO running";Tick();
            }
            catch(Exception ex){StopDemo();_status.Text=ex.GetType().Name;}
        }
        private void Tick()
        {
            if(!_running)return;
            if(_n>=1200){StopDemo();return;}
            try
            {
                double t=_n++*0.5,a=Math.Sin(t*0.35);
                var frame=new BavarianLiveFrame(true,"SIMULATED"){ElapsedSeconds=t};
                void Add(string job,double value,string unit)=>frame.Samples.Add(new BavarianSample(job,value,unit,DateTimeOffset.UtcNow,0));
                Add("STATUS_MOTORDREHZAHL",1300+500*a,"rpm");Add("STATUS_KUEHLMITTELTEMPERATUR",88,"deg C");
                Add("STATUS_LADEDRUCK_IST",1450+350*a,"hPa");Add("STATUS_LADEDRUCK_SOLL",1500+370*Math.Sin(t*0.35+0.1),"hPa");
                Add("STATUS_RAILDRUCK_IST",530+190*a,"bar");Add("STATUS_RAILDRUCK_SOLL",550+200*Math.Sin(t*0.35+0.1),"bar");Add("STATUS_UBATT",14.1,"V");
                foreach(var sample in frame.Samples)_csv.WriteLine(sample.ToCsv(true,frame.Variant));
                _csv.Flush();_panel.UpdateFrame(frame);_handler.PostDelayed(_tick,500);
            }
            catch(Exception ex){StopDemo();_status.Text=ex.GetType().Name;}
        }
        private void StopDemo(){_running=false;if(_tick!=null)_handler.RemoveCallbacks(_tick);_csv?.Dispose();_csv=null;if(_status!=null)_status.Text="DEMO stopped";}
        protected override void OnStop(){StopDemo();base.OnStop();}
        protected override void OnActivityResult(int requestCode,Result resultCode,Intent data)
        {
            base.OnActivityResult(requestCode,resultCode,data);
            if(requestCode!=612||resultCode!=Result.Ok||data?.Data==null||_file==null)return;
            try{using var input=File.OpenRead(_file);using var output=ContentResolver.OpenOutputStream(data.Data,"wt");if(output==null)throw new IOException();input.CopyTo(output);_status.Text="DEMO CSV exported";}
            catch(Exception ex){_status.Text=ex.GetType().Name;}
        }
    }
}
#endif
