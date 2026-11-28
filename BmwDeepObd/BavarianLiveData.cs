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
            canvas.DrawColor(BavarianUi.Surface);
            _paint.Color=BavarianUi.White;_paint.TextSize=13*d;_paint.StrokeWidth=1*d;
            canvas.DrawText(_caption,10*d,18*d,_paint);
            if(_history.Count==0)return;
            double lo=_history.Min(p=>Math.Min(p.A,p.B)),hi=_history.Max(p=>Math.Max(p.A,p.B));
            if(hi-lo<1){lo-=0.5;hi+=0.5;}
            float left=8*d,right=Width-8*d,top=37*d,bottom=Height-25*d;
            if(right<=left||bottom<=top)return;
            _paint.Color=BavarianUi.Muted;_paint.TextSize=10*d;
            canvas.DrawText(hi.ToString("0.0",CultureInfo.InvariantCulture),left,32*d,_paint);
            canvas.DrawText(lo.ToString("0.0",CultureInfo.InvariantCulture),left,Height-9*d,_paint);
            for(int i=1;i<_history.Count;i++)
            {
                float x1=left+(right-left)*(i-1)/Math.Max(1,_history.Count-1),x2=left+(right-left)*i/Math.Max(1,_history.Count-1);
                _paint.StrokeWidth=2*d;_paint.Color=BavarianUi.Blue;
                canvas.DrawLine(x1,bottom-(float)((_history[i-1].A-lo)/(hi-lo))*(bottom-top),x2,bottom-(float)((_history[i].A-lo)/(hi-lo))*(bottom-top),_paint);
                _paint.Color=BavarianUi.Compare;
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
            canvas.DrawColor(BavarianUi.Surface);
            _p.Color=BavarianUi.White;_p.TextSize=13*d;
            canvas.DrawText(_ru?"Коррекции цилиндров · тест":"Cylinder corrections · experimental",8*d,18*d,_p);
            if(_samples.Length!=6){canvas.DrawText(_ru?"Ожидание 6 значений":"Waiting for 6 values",8*d,40*d,_p);return;}
            _p.TextSize=11*d;
            canvas.DrawText((_ru?"Единица ECU: ":"ECU unit: ")+_samples[0].Unit,8*d,36*d,_p);
            double max=Math.Max(0.1,_samples.Max(s=>Math.Abs(s.Value)));
            float left=52*d,right=Width-78*d,mid=(left+right)/2;
            if(right<=left)return;
            float top=48*d,step=(Height-top-10*d)/6;
            _p.Color=BavarianUi.Muted;_p.StrokeWidth=d;
            canvas.DrawLine(mid,top,mid,Height-8*d,_p);
            for(int i=0;i<6;i++)
            {
                float y=top+i*step+step*0.68f;
                _p.Color=BavarianUi.White;_p.TextSize=12*d;
                canvas.DrawText((_ru?"Цил. ":"Cyl. ")+(i+1),8*d,y,_p);
                float x=mid+(float)(_samples[i].Value/max)*(right-left)/2;
                _p.Color=BavarianUi.Blue;
                canvas.DrawRect(Math.Min(mid,x),y-13*d,Math.Max(mid,x),y-2*d,_p);
                _p.Color=BavarianUi.White;
                canvas.DrawText(_samples[i].Value.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture),right+5*d,y,_p);
            }
        }
    }

    internal sealed class BavarianGauge : View
    {
        private readonly double _max;
        private double? _value;
        private readonly Paint _paint=new Paint(PaintFlags.AntiAlias);
        public BavarianGauge(Context c,double max):base(c){_max=max;}
        public void SetValue(double? value){if(_value==value)return;_value=value;Invalidate();}
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);float inset=6*Resources.DisplayMetrics.Density;
            float radius=Math.Min(Width/2f-inset,Height-inset);if(radius<=0)return;
            var rect=new RectF(Width/2f-radius,inset,Width/2f+radius,inset+radius*2);
            _paint.SetStyle(Paint.Style.Stroke);_paint.StrokeWidth=5*Resources.DisplayMetrics.Density;_paint.StrokeCap=Paint.Cap.Round;
            _paint.Color=BavarianUi.Line;canvas.DrawArc(rect,180,180,false,_paint);
            _paint.StrokeWidth=Resources.DisplayMetrics.Density;for(int i=0;i<=10;i++){double a=Math.PI+i*Math.PI/10;float cx=Width/2f,cy=inset+radius;float r=radius-7*Resources.DisplayMetrics.Density;if(r>0)canvas.DrawLine(cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r,cx+(float)Math.Cos(a)*(r-4*Resources.DisplayMetrics.Density),cy+(float)Math.Sin(a)*(r-4*Resources.DisplayMetrics.Density),_paint);} _paint.StrokeWidth=5*Resources.DisplayMetrics.Density;
            if(_value.HasValue){_paint.Color=BavarianUi.Blue;canvas.DrawArc(rect,180,(float)(180*Math.Clamp(_value.Value/_max,0,1)),false,_paint);}
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
        private readonly Dictionary<string,TextView> _units=new Dictionary<string,TextView>();
        private string[] _selection=Array.Empty<string>();
        private readonly Dictionary<string,BavarianGauge> _gauges=new Dictionary<string,BavarianGauge>();
        private int _displayMode;
        private readonly bool _gearbox;
        private TextView _slip;
        public void ToggleGauges(){_displayMode=(_displayMode+1)%3;Context.GetSharedPreferences("bavarian_dashboard",FileCreationMode.Private).Edit().PutInt("display_mode",_displayMode).Apply();ShowSelection(_selection);}
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);
        public BavarianLivePanel(Context context,bool gearbox):base(context)
        {
            _ru=BavarianLanguage.IsRussian(context);_gearbox=gearbox;var prefs=context.GetSharedPreferences("bavarian_dashboard",FileCreationMode.Private);_displayMode=Math.Clamp(prefs.GetInt("display_mode",prefs.GetBoolean("gauges",true)?0:1),0,2);
            Orientation=Orientation.Vertical;
            SetPadding(Dp(0),Dp(10),Dp(0),Dp(10));SetBackgroundColor(BavarianUi.Bg);
            _title=new TextView(context){TextSize=14};_title.SetTextColor(BavarianUi.Blue);AddView(_title);
            _tiles=new LinearLayout(context){Orientation=Orientation.Vertical};AddView(_tiles);
            if(gearbox){_slip=BavarianUi.Text(context,"",14,true);AddView(_slip);_slip.Visibility=ViewStates.Gone;}
            if(!gearbox)
            {
                _boost=new BavarianTrend(context,_ru?"Наддув / mbar · факт / цель":"Boost / mbar · actual / target");
                _rail=new BavarianTrend(context,_ru?"Рампа / bar · факт / цель":"Rail / bar · actual / target");
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
            _selection=jobs.ToArray();_tiles.RemoveAllViews();_readouts.Clear();_units.Clear();_gauges.Clear();
            if(_slip!=null){_slip.Visibility=jobs.Contains("STATUS_MOTORDREHZAHL")&&jobs.Contains("STATUS_TURBINENDREHZAHL")?ViewStates.Visible:ViewStates.Gone;_slip.Text=_ru?"Разность оборотов: —":"Speed difference: —";}
            bool injectors=jobs.Contains(BavarianLiveFrame.InjectorJob);
            if(_cylinders!=null){_cylinders.Visibility=injectors?ViewStates.Visible:ViewStates.Gone;_cylinders.ClearValues();}
            jobs=jobs.Where(j=>j!=BavarianLiveFrame.InjectorJob).ToArray();
            int columns=_displayMode==2?1:Resources.Configuration.Orientation==Android.Content.Res.Orientation.Landscape?3:2;
            LinearLayout row=null;
            for(int i=0;i<jobs.Length;i++)
            {
                if(i%columns==0)
                {
                    row=new LinearLayout(Context){Orientation=Orientation.Horizontal};
                    _tiles.AddView(row,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(8)});
                }
                var card=new LinearLayout(Context){Orientation=_displayMode==2?Orientation.Horizontal:Orientation.Vertical};card.SetGravity(GravityFlags.CenterVertical);card.SetPadding(Dp(10),Dp(9),Dp(10),Dp(9));
                card.Background=BavarianUi.Shape(Context,BavarianUi.Surface,14);
                var accent=new View(Context);accent.Background=BavarianUi.Shape(Context,BavarianUi.Blue,2);if(_displayMode!=2)card.AddView(accent,new LinearLayout.LayoutParams(Dp(24),Dp(3)){BottomMargin=Dp(7)});
                var lp=new LinearLayout.LayoutParams(0,ViewGroup.LayoutParams.WrapContent,1);lp.SetMargins(Dp(4),0,Dp(4),0);row.AddView(card,lp);
                var label=new TextView(Context){Text=BavarianLiveFrame.Label(jobs[i],_ru),TextSize=12};label.SetMinLines(_displayMode==2?1:2);label.SetTextColor(BavarianUi.Muted);if(_displayMode==2)card.AddView(label,new LinearLayout.LayoutParams(0,-2,1));else card.AddView(label);
                var readout=BavarianUi.Text(Context,"—",_displayMode==2?18:24,true);readout.SetTypeface(Typeface.Monospace,TypefaceStyle.Bold);readout.SetTextColor(BavarianUi.White);var reading=new LinearLayout(Context){Orientation=Orientation.Vertical};reading.AddView(readout);var unitLabel=BavarianUi.Text(Context,"",12);reading.AddView(unitLabel);card.AddView(reading);_units[jobs[i]]=unitLabel;
                _readouts[jobs[i]]=readout;
                double max=jobs[i] switch {"STATUS_MOTORDREHZAHL"=>5000,"STATUS_TURBINENDREHZAHL"=>5000,"STATUS_LADEDRUCK_IST" or "STATUS_LADEDRUCK_SOLL"=>3000,"STATUS_RAILDRUCK_IST" or "STATUS_RAILDRUCK_SOLL"=>2000,"STATUS_KUEHLMITTELTEMPERATUR" or "STATUS_GETRIEBETEMPERATUR"=>130,"STATUS_UBATT" or "STATUS_BATTERIESPANNUNG"=>16,_=>0};
                if(_displayMode==0&&max>0){var gauge=new BavarianGauge(Context,max);card.AddView(gauge,new LinearLayout.LayoutParams(-1,Dp(64)));_gauges[jobs[i]]=gauge;}
            }
            if(row!=null&&jobs.Length%columns!=0)
                for(int i=jobs.Length%columns;i<columns;i++)row.AddView(new View(Context),new LinearLayout.LayoutParams(0,1,1));
            _boost?.ClearHistory();_rail?.ClearHistory();
            if(_boost!=null)_boost.Visibility=jobs.Contains("STATUS_LADEDRUCK_IST")&&jobs.Contains("STATUS_LADEDRUCK_SOLL")?ViewStates.Visible:ViewStates.Gone;
            if(_rail!=null)_rail.Visibility=jobs.Contains("STATUS_RAILDRUCK_IST")&&jobs.Contains("STATUS_RAILDRUCK_SOLL")?ViewStates.Visible:ViewStates.Gone;
            _title.Text=_ru?"Ожидание данных":"Waiting for readings";
        }
        public void PrepareSession(string[] jobs){if(!_selection.SequenceEqual(jobs))ShowSelection(jobs);else ResetHistory();}
        public void ResetHistory(){_cylinders?.ClearValues();foreach(var value in _readouts.Values)value.Text="—";foreach(var u in _units.Values)u.Text="";foreach(var gauge in _gauges.Values)gauge.SetValue(null);if(_slip!=null)_slip.Text=_ru?"Разность оборотов: —":"Speed difference: —";_title.Text=_ru?"Ожидание данных":"Waiting for readings";_boost?.ClearHistory();_rail?.ClearHistory();}
        public void MarkStopped(){_title.Text=(_ru?"Остановлено · последние показания":"Stopped · last readings");}
        public void UpdateFrame(BavarianLiveFrame frame)
        {
            if(_selection.Length==0)ShowSelection(frame.Samples.Select(s=>s.Job).ToArray());
            _title.Text=(frame.Demo?"DEMO · simulated":"ECU · "+frame.Variant)+" · "+frame.ElapsedSeconds.ToString("0.0",CultureInfo.InvariantCulture)+" s";
            foreach(var s in frame.Samples)
            {
                if(!_readouts.TryGetValue(s.Job,out var value))continue;
                if(_gauges.TryGetValue(s.Job,out var gauge))gauge.SetValue(s.Value);
                string unit=s.Unit=="deg C"||s.Unit=="Grad C"?"°C":s.Unit=="hPa"?"mbar":s.Unit;
                string fmt=s.Job=="STATUS_MOTORDREHZAHL"||s.Job=="STATUS_ISTGANG"?"0":s.Job=="STATUS_UBATT"?"0.00":"0.0";
                string next=string.IsNullOrWhiteSpace(s.StateText)?s.Value.ToString(fmt,CultureInfo.InvariantCulture):s.Value.ToString("0",CultureInfo.InvariantCulture);if(value.Text!=next)value.Text=next;
                if(_units.TryGetValue(s.Job,out var unitView)){string detail=string.IsNullOrWhiteSpace(s.StateText)?unit:s.StateText;if(unitView.Text!=detail)unitView.Text=detail;}
            }
            if(_slip!=null&&_slip.Visibility==ViewStates.Visible)
            {
                var engine=frame.Samples.FirstOrDefault(s=>s.Job=="STATUS_MOTORDREHZAHL");
                var turbine=frame.Samples.FirstOrDefault(s=>s.Job=="STATUS_TURBINENDREHZAHL");
                bool valid=engine!=null&&turbine!=null&&engine.Unit=="U/min"&&turbine.Unit=="U/min"&&Math.Abs((engine.Time-turbine.Time).TotalMilliseconds)<=500;
                _slip.Text=(_ru?"Разность оборотов (последовательные замеры): ":"Speed difference (sequential readings): ")+(valid?(engine.Value-turbine.Value).ToString("0",CultureInfo.InvariantCulture)+" rpm":"—");
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
        private bool _demoRu;
        private string DT(string en,string ru)=>_demoRu?ru:en;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            BavarianUi.ApplyTheme(this);base.OnCreate(savedInstanceState);
            SupportActionBar?.Hide();
            _demoRu=BavarianLanguage.IsRussian(this);
            BavarianUi.Shell(this,2,_demoRu,i=>{StopDemo();BavarianUi.NavigateHome(this,i);},out var body);
            body.AddView(BavarianUi.Text(this,DT("DEMO · SIMULATED DATA · NO VEHICLE CONNECTION","DEMO · ИМИТАЦИЯ · НЕТ СВЯЗИ С АВТО"),18,true));
            var start=new Button(this){Text=DT("Start demo + CSV","Запустить DEMO + CSV")};BavarianUi.Style(start,true);body.AddView(start);start.Click+=(s,e)=>StartDemo();
            var stop=new Button(this){Text=DT("Stop demo","Остановить DEMO")};BavarianUi.Style(stop);body.AddView(stop);stop.Click+=(s,e)=>StopDemo();
            var export=new Button(this){Text=DT("Export demo CSV","Экспорт DEMO CSV")};BavarianUi.Style(export);body.AddView(export);export.Click+=(s,e)=>{
                StopDemo();
                if(_file==null||!File.Exists(_file))return;
                var intent=new Intent(Intent.ActionCreateDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("text/csv");
                intent.PutExtra(Intent.ExtraTitle,"bavarian-DEMO.csv");StartActivityForResult(intent,612);
            };
            _status=BavarianUi.Text(this,"",14);body.AddView(_status);
            _panel=new BavarianLivePanel(this,_demoRu);body.AddView(_panel);
            BavarianUi.Action(body,DT("Fault scan simulator","Имитация сканирования ошибок"),(s,e)=>{StopDemo();ShowFaultDemo();});
            _handler=new Handler(Looper.MainLooper);_tick=new Java.Lang.Runnable(Tick);
        }
        private void ShowFaultDemo()
        {
            var labels=new[]{DT("Mixed fault records","Разные статусы ошибок"),DT("Partial scan / timeout","Частичный опрос / тайм-аут"),DT("Connection lost / cancelled","Связь потеряна / отменено")};
            new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("DEMO").SetItems(labels,(d,a)=>{
                var panel=new LinearLayout(this){Orientation=Orientation.Vertical};
                panel.AddView(BavarianUi.Text(this,DT("SIMULATED SCAN · no ECU jobs, no saved vehicle history","ИМИТАЦИЯ · без запросов ECU и записи в историю"),15,true));
                var faults=new BavarianFaultPanel(this,_demoRu);panel.AddView(faults);faults.BeginScan(new[]{"DDE","EGS","CAS"});
                List<Dictionary<string,string>> Rows()=>new()
                {
                    new(){["F_VERSION"]="2",["F_HEX_CODE"]="450100",["F_ORT_TEXT"]="DEMO EGR",["F_VORHANDEN_NR"]="33"},
                    new(){["F_VERSION"]="2",["F_HEX_CODE"]="452100",["F_ORT_TEXT"]="DEMO boost",["F_VORHANDEN_NR"]="35"},
                    new(){["F_VERSION"]="2",["F_HEX_CODE"]="458700",["F_ORT_TEXT"]="DEMO fuel filter",["F_VORHANDEN_NR"]="32"}
                };
                faults.AddResult("DDE","d50m57e0.prg",Rows());
                if(a.Which==0){faults.AddResult("EGS","gs19a.prg",new());faults.AddResult("CAS","cas.prg",new());}
                else if(a.Which==1){faults.AddFailure("EGS",DT("Simulated timeout","Имитация тайм-аута"));faults.AddResult("CAS","cas.prg",new());}
                else faults.AddFailure("EGS",DT("Simulated adapter disconnect","Имитация отключения адаптера"));
                var scroll=new ScrollView(this);scroll.AddView(panel);
                new AndroidX.AppCompat.App.AlertDialog.Builder(this).SetTitle("DEMO · "+labels[a.Which]).SetView(scroll).SetPositiveButton(DT("Close","Закрыть"),(dd,aa)=>{}).Show();
            }).Show();
        }
        private void StartDemo()
        {
            if(_running)return;
            try
            {
                _file=Path.Combine(FilesDir.AbsolutePath,"bavarian-DEMO.csv");
                _csv=new StreamWriter(_file,false,new UTF8Encoding(false));_csv.WriteLine(BavarianLiveFrame.CsvHeader);
                _n=0;_panel.ResetHistory();_running=true;_status.Text=DT("DEMO running","DEMO запущен");Tick();
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
        private void StopDemo(){_running=false;if(_tick!=null)_handler.RemoveCallbacks(_tick);_csv?.Dispose();_csv=null;_panel?.MarkStopped();if(_status!=null)_status.Text=DT("DEMO stopped","DEMO остановлен");}
        protected override void OnStop(){StopDemo();base.OnStop();}
        protected override void OnActivityResult(int requestCode,Result resultCode,Intent data)
        {
            base.OnActivityResult(requestCode,resultCode,data);
            if(requestCode!=612||resultCode!=Result.Ok||data?.Data==null||_file==null)return;
            try{using var input=File.OpenRead(_file);using var output=ContentResolver.OpenOutputStream(data.Data,"wt");if(output==null)throw new IOException();input.CopyTo(output);_status.Text=DT("DEMO CSV exported","DEMO CSV сохранён");}
            catch(Exception ex){_status.Text=ex.GetType().Name;}
        }
    }
}
#endif
