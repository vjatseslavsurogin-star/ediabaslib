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
        public readonly string Job, Unit, StateText;
        public readonly double Value;
        public readonly DateTimeOffset Time;
        public readonly long Latency;
        public BavarianSample(string job,double value,string unit,DateTimeOffset time,long latency,string stateText="")
        {Job=job;Value=value;Unit=unit;Time=time;Latency=latency;StateText=stateText;}
        private static string Quote(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        public string ToCsv(bool demo,string variant)=>string.Join(",",new[]{
            Quote(Time.ToString("o",CultureInfo.InvariantCulture)),demo?"DEMO":"ECU",Quote(variant),Quote(Job),
            Value.ToString("R",CultureInfo.InvariantCulture),Quote(Unit),demo?"":Latency.ToString(CultureInfo.InvariantCulture),Quote(StateText)});
    }

    internal sealed class BavarianLiveFrame
    {
        public const string CsvHeader="timestamp_utc,source,variant,job,value,unit,latency_ms,ecu_text";
        public const string InjectorJob="STATUS_LAUFUNRUHE_LLR_MENGE";
        public static string Cylinder(int i)=>InjectorJob+"_ZYL"+i;
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
                "STATUS_LUFTMASSE_IST"=>ru?"Масса воздуха · факт":"Air mass · actual",
                "STATUS_LUFTMASSE_SOLL"=>ru?"Масса воздуха · цель":"Air mass · target",
                "STATUS_ATMOSPHAERENDRUCK"=>ru?"Атмосферное давление":"Atmospheric pressure",
                "STATUS_ANSAUGLUFTTEMPERATUR"=>ru?"Температура впуска":"Intake air temperature",
                "STATUS_KRAFTSTOFFTEMPERATUR"=>ru?"Температура топлива":"Fuel temperature",
                "STATUS_PWG_POTI_1"=>ru?"Датчик педали 1":"Pedal sensor 1",
                InjectorJob=>ru?"Коррекции цилиндров 1–6":"Cylinder corrections 1–6",
                "STATUS_WH_POSITION"=>ru?"Положение селектора":"Selector position",
                "STATUS_GETRIEBEPOSITION"=>ru?"Положение коробки":"Transmission position",
                "STATUS_TURBINENDREHZAHL"=>ru?"Обороты турбинного вала":"Turbine speed",
                "STATUS_ABTRIEBSDREHZAHL"=>ru?"Обороты выходного вала":"Output shaft speed",
                "STATUS_FAHRPEDALWINKEL"=>ru?"Педаль акселератора":"Accelerator position",
                "STATUS_MOTORISTMOMENT"=>ru?"Момент двигателя · факт (EGS)":"Engine torque actual (EGS)",
                "STATUS_MOTORSOLLMOMENT"=>ru?"Момент двигателя · цель (EGS)":"Engine torque target (EGS)",
                "STATUS_BATTERIESPANNUNG"=>ru?"Напряжение EGS":"EGS voltage",
                "STATUS_WK"=>ru?"Муфта гидротрансформатора":"Converter clutch",
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
        private static string SingleField(List<Dictionary<string,string>> rows,string field,bool required)
        {
            var values=rows.Where(r=>r.ContainsKey(field)).Select(r=>Get(r,field)).Distinct(StringComparer.Ordinal).ToArray();
            if(values.Length>1||(required&&(values.Length!=1||string.IsNullOrWhiteSpace(values[0]))))
                throw new IOException("Missing or ambiguous ECU field: "+field);
            return values.FirstOrDefault()??"";
        }
        public static List<BavarianSample> ParseSamples(string job,string variant,List<Dictionary<string,string>> rows,long latency,bool experimental)
        {
            string key="STAT_"+job.Substring(7);
            bool state=job=="STATUS_ISTGANG"||job=="STATUS_WH_POSITION"||job=="STATUS_GETRIEBEPOSITION"||job=="STATUS_WK";
            string unit=SingleField(rows,key+"_EINH",!state);
            string stateText=SingleField(rows,key+"_TEXT",false);
            if(state&&!string.IsNullOrEmpty(unit))throw new IOException("Unexpected unit for state: "+job);
            if(!experimental&&!UnitMatches(job,unit))throw new IOException("Unexpected unit for "+job+": "+unit);
            if(experimental&&!state&&string.IsNullOrWhiteSpace(unit))throw new IOException("No ECU unit: "+job);
            var samples=new List<BavarianSample>();
            var now=DateTimeOffset.UtcNow;
            void Add(string id,string field)
            {
                string raw=SingleField(rows,field,true);
                if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double numeric)||!double.IsFinite(numeric))
                    throw new IOException("Unexpected numeric result: "+field);
                samples.Add(new BavarianSample(id,numeric,unit,now,latency,stateText));
            }
            if(job==InjectorJob)
            {
                if(variant!="d50m57e0.prg")throw new IOException("Cylinder profile not approved");
                for(int i=1;i<=6;i++)Add(Cylinder(i),key+"_ZYL"+i+"_WERT");
            }
            else Add(job,key+"_WERT");
            return samples;
        }
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

    internal sealed class BavarianCylinderView : View
    {
        private readonly Paint _p=new Paint(PaintFlags.AntiAlias);
        private readonly bool _ru;
        private BavarianSample[] _samples=Array.Empty<BavarianSample>();
        public BavarianCylinderView(Context context,bool ru):base(context){_ru=ru;}
        public void ClearValues(){_samples=Array.Empty<BavarianSample>();Invalidate();}
        public void UpdateValues(BavarianLiveFrame frame)
        {
            _samples=Enumerable.Range(1,6).Select(i=>frame.Samples.FirstOrDefault(s=>s.Job==BavarianLiveFrame.Cylinder(i))).Where(s=>s!=null).ToArray();
            Invalidate();
        }
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float d=Resources.DisplayMetrics.Density;
            canvas.DrawColor(Color.Rgb(22,32,49));
            _p.Color=Color.White;_p.TextSize=13*d;
            canvas.DrawText(_ru?"Коррекции цилиндров · тест":"Cylinder corrections · experimental",8*d,18*d,_p);
            if(_samples.Length!=6){canvas.DrawText(_ru?"Ожидание 6 значений":"Waiting for 6 values",8*d,40*d,_p);return;}
            _p.TextSize=11*d;
            canvas.DrawText((_ru?"Единица ECU: ":"ECU unit: ")+_samples[0].Unit,8*d,36*d,_p);
            double max=Math.Max(0.1,_samples.Max(s=>Math.Abs(s.Value)));
            float left=52*d,right=Width-78*d,mid=(left+right)/2;
            if(right<=left)return;
            float top=48*d,step=(Height-top-10*d)/6;
            _p.Color=Color.Rgb(135,155,180);_p.StrokeWidth=d;
            canvas.DrawLine(mid,top,mid,Height-8*d,_p);
            for(int i=0;i<6;i++)
            {
                float y=top+i*step+step*0.68f;
                _p.Color=Color.White;_p.TextSize=12*d;
                canvas.DrawText((_ru?"Цил. ":"Cyl. ")+(i+1),8*d,y,_p);
                float x=mid+(float)(_samples[i].Value/max)*(right-left)/2;
                _p.Color=Color.Rgb(103,179,255);
                canvas.DrawRect(Math.Min(mid,x),y-13*d,Math.Max(mid,x),y-2*d,_p);
                _p.Color=Color.White;
                canvas.DrawText(_samples[i].Value.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture),right+5*d,y,_p);
            }
        }
    }

    internal sealed class BavarianLivePanel : LinearLayout
    {
        private readonly bool _ru;
        private readonly TextView _title;
        private readonly LinearLayout _tiles;
        private readonly BavarianTrend _boost,_rail;
        private readonly BavarianCylinderView _cylinders;
        private readonly Dictionary<string,TextView> _readouts=new Dictionary<string,TextView>();
        private string[] _selection=Array.Empty<string>();
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);
        public BavarianLivePanel(Context context,bool gearbox):base(context)
        {
            _ru=BavarianLanguage.IsRussian(context);
            Orientation=Orientation.Vertical;
            SetPadding(Dp(6),Dp(10),Dp(6),Dp(10));SetBackgroundColor(Color.Rgb(12,20,34));
            _title=new TextView(context){TextSize=14};_title.SetTextColor(Color.Rgb(103,179,255));AddView(_title);
            _tiles=new LinearLayout(context){Orientation=Orientation.Vertical};AddView(_tiles);
            if(!gearbox)
            {
                _boost=new BavarianTrend(context,"Boost / mbar · actual blue · target amber");
                _rail=new BavarianTrend(context,"Rail / bar · actual blue · target amber");
                AddView(_boost,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,Dp(150)){TopMargin=Dp(8)});
                AddView(_rail,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,Dp(150)){TopMargin=Dp(8)});
                _boost.Visibility=_rail.Visibility=ViewStates.Gone;
                _cylinders=new BavarianCylinderView(context,_ru);
                AddView(_cylinders,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,Dp(250)){TopMargin=Dp(8)});
                _cylinders.Visibility=ViewStates.Gone;
            }
        }
        public void ShowSelection(string[] jobs)
        {
            _selection=jobs.ToArray();_tiles.RemoveAllViews();_readouts.Clear();
            bool injectors=jobs.Contains(BavarianLiveFrame.InjectorJob);
            if(_cylinders!=null){_cylinders.Visibility=injectors?ViewStates.Visible:ViewStates.Gone;_cylinders.ClearValues();}
            jobs=jobs.Where(j=>j!=BavarianLiveFrame.InjectorJob).ToArray();
            int columns=Resources.Configuration.Orientation==Android.Content.Res.Orientation.Landscape?3:2;
            LinearLayout row=null;
            for(int i=0;i<jobs.Length;i++)
            {
                if(i%columns==0)
                {
                    row=new LinearLayout(Context){Orientation=Orientation.Horizontal};
                    _tiles.AddView(row,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(8)});
                }
                var card=new LinearLayout(Context){Orientation=Orientation.Vertical};card.SetPadding(Dp(10),Dp(9),Dp(10),Dp(9));
                var bg=new Android.Graphics.Drawables.GradientDrawable();bg.SetColor(Color.Rgb(25,40,59));bg.SetCornerRadius(Dp(10));card.Background=bg;
                var lp=new LinearLayout.LayoutParams(0,ViewGroup.LayoutParams.WrapContent,1);lp.SetMargins(Dp(4),0,Dp(4),0);row.AddView(card,lp);
                var label=new TextView(Context){Text=BavarianLiveFrame.Label(jobs[i],_ru),TextSize=12};label.SetMinLines(2);label.SetTextColor(Color.Rgb(178,195,215));card.AddView(label);
                var readout=new TextView(Context){Text="—",TextSize=25};readout.SetTextColor(Color.White);card.AddView(readout);
                _readouts[jobs[i]]=readout;
            }
            if(row!=null&&jobs.Length%columns!=0)
                for(int i=jobs.Length%columns;i<columns;i++)row.AddView(new View(Context),new LinearLayout.LayoutParams(0,1,1));
            _boost?.ClearHistory();_rail?.ClearHistory();
            if(_boost!=null)_boost.Visibility=jobs.Contains("STATUS_LADEDRUCK_IST")&&jobs.Contains("STATUS_LADEDRUCK_SOLL")?ViewStates.Visible:ViewStates.Gone;
            if(_rail!=null)_rail.Visibility=jobs.Contains("STATUS_RAILDRUCK_IST")&&jobs.Contains("STATUS_RAILDRUCK_SOLL")?ViewStates.Visible:ViewStates.Gone;
            _title.Text=_ru?"Ожидание данных":"Waiting for readings";
        }
        public void ResetHistory(){_cylinders?.ClearValues();foreach(var value in _readouts.Values)value.Text="—";_title.Text="Waiting for readings";_boost?.ClearHistory();_rail?.ClearHistory();}
        public void MarkStopped(){_title.Text=(_ru?"Остановлено · последние показания":"Stopped · last readings");}
        public void UpdateFrame(BavarianLiveFrame frame)
        {
            if(_selection.Length==0)ShowSelection(frame.Samples.Select(s=>s.Job).ToArray());
            _title.Text=(frame.Demo?"DEMO · simulated":"ECU · "+frame.Variant)+" · "+frame.ElapsedSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" s";
            foreach(var s in frame.Samples)
            {
                if(!_readouts.TryGetValue(s.Job,out var value))continue;
                string unit=s.Unit=="deg C"||s.Unit=="Grad C"?"°C":s.Unit=="hPa"?"mbar":s.Unit;
                string fmt=s.Job=="STATUS_MOTORDREHZAHL"||s.Job=="STATUS_ISTGANG"?"0":s.Job=="STATUS_UBATT"?"0.00":"0.0";
                value.Text=string.IsNullOrWhiteSpace(s.StateText)?s.Value.ToString(fmt,CultureInfo.InvariantCulture)+" "+unit:s.StateText+" ("+s.Value.ToString("0",CultureInfo.InvariantCulture)+")";
            }
            _cylinders?.UpdateValues(frame);
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
            BavarianUi.Shell(this,2,BavarianLanguage.IsRussian(this),i=>{StopDemo();BavarianUi.NavigateHome(this,i);},out var body);
            body.AddView(BavarianUi.Text(this,"DEMO · SIMULATED DATA · NO VEHICLE CONNECTION",18,true));
            var start=new Button(this){Text="Start demo + CSV"};BavarianUi.Style(start,true);body.AddView(start);start.Click+=(s,e)=>StartDemo();
            var stop=new Button(this){Text="Stop demo"};BavarianUi.Style(stop);body.AddView(stop);stop.Click+=(s,e)=>StopDemo();
            var export=new Button(this){Text="Export demo CSV"};BavarianUi.Style(export);body.AddView(export);export.Click+=(s,e)=>{
                StopDemo();
                if(_file==null||!File.Exists(_file))return;
                var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("text/csv");
                intent.PutExtra(Intent.ExtraTitle,"bavarian-DEMO.csv");StartActivityForResult(intent,612);
            };
            _status=BavarianUi.Text(this,"",14);body.AddView(_status);
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
        private void StopDemo(){_running=false;if(_tick!=null)_handler.RemoveCallbacks(_tick);_csv?.Dispose();_csv=null;_panel?.MarkStopped();if(_status!=null)_status.Text="DEMO stopped";}
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
