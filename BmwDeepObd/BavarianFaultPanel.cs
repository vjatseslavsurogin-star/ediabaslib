#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
namespace BmwDeepObd
{
    internal sealed class BavarianFaultPanel : LinearLayout
    {
        private sealed class ModuleResult
        {
            public string Module,Variant,Error,Vin,ExtraNote;
            public Dictionary<int,List<Dictionary<string,string>>> Details;
            public List<Dictionary<string,string>> Records;
        }
        private readonly bool _ru;
        private readonly List<ModuleResult> _results=new List<ModuleResult>();
        private readonly Spinner _filter;
        private readonly CheckBox _moduleStatus;
        private readonly LinearLayout _cards;
        private readonly TextView _summary;
        private readonly TextView[] _counts=new TextView[3];
        private readonly LinearLayout _countRow;
        private string[] _planned=Array.Empty<string>();
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);
        public BavarianFaultPanel(Context context,bool ru):base(context)
        {
            _ru=ru;Orientation=Orientation.Vertical;
            _summary=TextLine(this,T("No scan performed.","Чтение ещё не выполнено."),16);
            _summary.Background=BavarianUi.Shape(context,BavarianUi.Surface);_summary.SetPadding(Dp(14),Dp(12),Dp(14),Dp(12));
            _countRow=new LinearLayout(context){Orientation=Orientation.Horizontal};
            string[] labels={T("Active","Активные"),T("Stored","Сохранённые"),T("Other","Прочие")};
            Color[] colors={BavarianUi.Danger,BavarianUi.Warning,BavarianUi.Muted};
            for(int i=0;i<3;i++)
            {
                var cell=new LinearLayout(context){Orientation=Orientation.Vertical};cell.SetPadding(Dp(8),Dp(10),Dp(8),Dp(10));cell.Background=BavarianUi.Shape(context,BavarianUi.Surface);
                _counts[i]=BavarianUi.Text(context,"—",24,true);_counts[i].SetTextColor(colors[i]);cell.AddView(_counts[i]);
                cell.AddView(BavarianUi.Text(context,labels[i],11));_countRow.AddView(cell,new LinearLayout.LayoutParams(0,-2,1){RightMargin=Dp(4)});
            }
            AddView(_countRow,new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(8)});
            _filter=new Spinner(context);var filterAdapter=new ArrayAdapter<string>(context,Android.Resource.Layout.SimpleSpinnerItem,new[]{T("Relevant fault records","Актуальные записи"),T("Active","Активные"),T("Stored / not current","Сохранённые / не активные"),T("All records","Все записи")});
            filterAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);_filter.Adapter=filterAdapter;AddView(_filter);
            _moduleStatus=new CheckBox(context){Text=T("Show module status and technical details","Показать статус блоков и технические данные")};BavarianUi.Checkbox(_moduleStatus);AddView(_moduleStatus);
            _cards=new LinearLayout(context){Orientation=Orientation.Vertical};AddView(_cards);
            _filter.ItemSelected+=(s,e)=>Render();_moduleStatus.CheckedChange+=(s,e)=>Render();
        }
        private TextView TextLine(LinearLayout parent,string text,int size)
        {
            var t=BavarianUi.Text(Context,text,size,size>=18);t.SetTextIsSelectable(true);parent.AddView(t);return t;
        }
        private static string Get(Dictionary<string,string> r,string k)=>r.TryGetValue(k,out string value)?value??"":"";
        public void BeginScan(string[] modules){_planned=modules.ToArray();_results.Clear();Render();}
        public void AddFailure(string module,string error)
        {_results.RemoveAll(m=>m.Module==module);_results.Add(new ModuleResult{Module=module,Error=error});Render();}
        internal static List<Dictionary<string,string>> ParseRecords(List<Dictionary<string,string>> rows)
        {
            var records=new List<Dictionary<string,string>>();
            Dictionary<string,string> current=null;
            string version="";
            foreach(var r in rows??new List<Dictionary<string,string>>())
            {
                if(r.TryGetValue("F_VERSION",out string vers))version=vers;
                if(r.ContainsKey("F_HEX_CODE")||(r.ContainsKey("F_ORT_NR")&&(current==null||current.ContainsKey("F_ORT_NR"))))
                {
                    if(current!=null)records.Add(current);
                    current=new Dictionary<string,string>(r);
                    if(!current.ContainsKey("F_VERSION")&&!string.IsNullOrEmpty(version))current["F_VERSION"]=version;
                }
                else if(current!=null)
                    foreach(var kv in r)if(kv.Key.StartsWith("F_",StringComparison.Ordinal))current[kv.Key]=kv.Value;
            }
            if(current!=null)records.Add(current);
            return records;
        }
        public void AddResult(string module,string variant,List<Dictionary<string,string>> rows,Dictionary<int,List<Dictionary<string,string>>> details=null,string vin=null,string note=null)
        {
            _results.RemoveAll(m=>m.Module==module);_results.Add(new ModuleResult{Module=module,Variant=variant,Records=ParseRecords(rows),Details=details,Vin=vin,ExtraNote=note});Render();
        }
        public string ExportText()
        {
            var lines=new List<string>();
            foreach(string planned in _planned.Where(p=>!_results.Any(m=>m.Module==p)))lines.Add(planned+" · not read");
            foreach(var m in _results)
            {
                lines.Add("\n"+m.Module+" · "+m.Variant);
                if(m.Error!=null){lines.Add("Read failed: "+m.Error);continue;}
                if(m.Vin!=null)lines.Add("VIN: "+m.Vin);
                if(m.ExtraNote!=null)lines.Add(m.ExtraNote);
                if(m.Records.Count==0)lines.Add("No recognized records");
                foreach(var record in m.Records)
                {
                    lines.Add("\n"+Code(record));
                    lines.AddRange(record.Select(k=>k.Key+": "+k.Value));
                }
                if(m.Details!=null)foreach(var pair in m.Details)foreach(var record in pair.Value)
                {
                    lines.Add("\nDETAIL "+pair.Key.ToString("X4"));
                    lines.AddRange(record.Select(k=>k.Key+": "+k.Value));
                }
            }
            return string.Join("\n",lines);
        }
        private static string Code(Dictionary<string,string> r)
        {
            if(int.TryParse(Get(r,"F_ORT_NR"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))return n.ToString("X4");
            string raw=Get(r,"F_HEX_CODE");
            return raw.Length==6?raw.Substring(0,4):raw;
        }
        private string Description(ModuleResult m,string code,Dictionary<string,string> f)
        {
            if(m.Variant=="d50m57e0.prg")
                switch(code)
                {
                    case "4501":return T("EGR control deviation — air mass too high","EGR — отклонение регулирования, масса воздуха слишком высокая");
                    case "4587":return T("Fuel filter — clogged","Топливный фильтр — засорение");
                    case "4548":return T("Power management — closed-circuit current","Энергоменеджмент — ток покоя");
                    case "41AB":return T("Boost pressure too low / implausible","Давление наддува слишком низкое / неправдоподобное");
                    case "3F57":return T("Boost actuator fault or overheating","Регулятор наддува — неисправность или перегрев");
                    case "4521":return T("Boost control deviation — pressure too high","Отклонение регулирования наддува — давление слишком высокое");
                }
            if(m.Variant=="gs19a.prg"&&code=="5150")return T("Trailer module CAN message missing","Отсутствует CAN-сообщение от модуля прицепа");
            return Get(f,"F_ORT_TEXT")+"\n"+Get(f,"F_SYMPTOM_TEXT");
        }
        private static string State(Dictionary<string,string> r)=>Get(r,"F_VERSION")=="2"?Get(r,"F_VORHANDEN_NR"):"";
        private string Status(string value)=>value switch {
            "35"=>T("ACTIVE / STORED","АКТИВНАЯ / СОХРАНЕНА"),
            "33"=>T("STORED / NOT CURRENT","СОХРАНЕНА / НЕ АКТИВНА"),
            "32"=>T("NOT OCCURRED","НЕ ВОЗНИКАЛА"),
            _=>T("UNKNOWN STATUS","НЕИЗВЕСТНЫЙ СТАТУС")};
        private void Render()
        {
            _cards.RemoveAllViews();
            var records=_results.Where(m=>m.Records!=null).SelectMany(m=>m.Records).ToList();
            bool any=_results.Any(m=>m.Records!=null&&m.Records.Count>0);
            _counts[0].Text=any?records.Count(f=>State(f)=="35").ToString():"—";
            _counts[1].Text=any?records.Count(f=>State(f)=="33").ToString():"—";
            _counts[2].Text=any?records.Count(f=>State(f)!="35"&&State(f)!="33").ToString():"—";
            _summary.Text=T("Groups read: ","Групп прочитано: ")+_results.Count(m=>m.Records!=null)+"/"+_planned.Length+" · "+T("Failed / unconfirmed: ","Не прочитано / не подтверждено: ")+_results.Count(m=>m.Error!=null)+"\n"+T("Recognized records only; groups may overlap.","Только распознанные записи; группы могут пересекаться.");
            if(_moduleStatus.Checked)foreach(string planned in _planned.Where(p=>!_results.Any(m=>m.Module==p)))TextLine(_cards,planned+" · "+T("Not read yet","Не прочитан"),13);
            int shown=0;
            foreach(var m in _results)
            {
                if(_moduleStatus.Checked)TextLine(_cards,m.Module+(m.Variant==null?"":" · "+m.Variant),18);
                if(m.Error!=null){if(_moduleStatus.Checked)TextLine(_cards,T("Read failed: ","Не прочитан: ")+m.Error,14);continue;}
                if(_moduleStatus.Checked&&!string.IsNullOrEmpty(m.Vin))TextLine(_cards,"VIN: "+m.Vin,15);
                if(_moduleStatus.Checked&&!string.IsNullOrEmpty(m.ExtraNote))TextLine(_cards,m.ExtraNote,13);
                if(m.Records.Count==0)
                {if(_moduleStatus.Checked)TextLine(_cards,T("No recognizable records; check raw report.","Распознанных записей нет; проверьте исходный отчёт."),14);continue;}
                var visible=m.Records.Where(f=>_filter.SelectedItemPosition switch {1=>State(f)=="35",2=>State(f)=="33",3=>true,_=>State(f)!="32"})
                    .OrderBy(f=>State(f)=="35"?0:State(f)=="33"?1:2).ToList();
                shown+=visible.Count;
                if(_moduleStatus.Checked&&visible.Count==0)TextLine(_cards,T("No records matching filter.","Нет записей по фильтру."),14);
                foreach(var f in visible)
                {
                    string code=Code(f),state=State(f);
                    var card=new LinearLayout(Context){Orientation=Orientation.Vertical};
                    card.SetPadding(Dp(12),Dp(10),Dp(12),Dp(10));card.Background=BavarianUi.Shape(Context,BavarianUi.Surface);
                    var lp=new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(8)};
                    _cards.AddView(card,lp);
                    var title=TextLine(card,m.Module+" · "+code,20);title.SetTextColor(BavarianUi.Blue);
                    var status=TextLine(card,Status(state),13);
                    status.SetTextColor(state=="35"?BavarianUi.Danger:state=="33"?BavarianUi.Warning:BavarianUi.Muted);
                    TextLine(card,Description(m,code,f),16).SetTextColor(BavarianUi.White);
                    if(Get(f,"F_VERSION")=="2"&&Get(f,"F_WARNUNG_NR")=="49")
                        TextLine(card,T("May request a warning lamp; actual MIL state not measured.","Может вызвать контрольную лампу; фактическое состояние MIL не измерено."),13).SetTextColor(BavarianUi.Warning);
                    var details=new Button(Context){Text=T("Details  ›","Подробнее  ›")};BavarianUi.Style(details);card.AddView(details);
                    var expanded=new LinearLayout(Context){Orientation=Orientation.Vertical,Visibility=ViewStates.Gone};card.AddView(expanded);
                    string original=string.Join("\n",f.Select(k=>k.Key+": "+k.Value));
                    if(state=="")TextLine(expanded,Get(f,"F_VORHANDEN_TEXT"),13).SetTextColor(BavarianUi.Muted);
                    if(int.TryParse(Get(f,"F_ORT_NR"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int codeNum)&&m.Details!=null&&m.Details.TryGetValue(codeNum,out var more)&&more.Count==1)
                    {
                        var d=more[0];
                        if(!string.IsNullOrWhiteSpace(Get(d,"F_PCODE_STRING"))&&Get(d,"F_PCODE_STRING")!="--"&&Get(d,"F_PCODE_STRING")!="??")
                            TextLine(expanded,Get(d,"F_PCODE_STRING")+" · "+Get(d,"F_PCODE_TEXT"),14).SetTextColor(BavarianUi.White);
                        if(int.TryParse(Get(d,"F_HFK"),out int count)&&count>=0)
                            TextLine(expanded,T("Occurrence count: ","Счётчик возникновения: ")+count,14).SetTextColor(BavarianUi.White);
                        if(long.TryParse(Get(d,"F_UW_KM"),out long km)&&km>=0)
                            TextLine(expanded,T("Stored mileage: ","Пробег в памяти: ")+km+" km",14).SetTextColor(BavarianUi.White);
                        original+="\n\nFS_LESEN_DETAIL\n"+string.Join("\n",d.Select(k=>k.Key+": "+k.Value));
                    }
                    BavarianUi.Action(expanded,T("Raw ECU data","Исходные данные ECU"),(s,e)=>new AndroidX.AppCompat.App.AlertDialog.Builder(Context).SetTitle(m.Module+" · "+code)
                        .SetMessage(original).SetPositiveButton("OK",(d,a)=>{}).Show());
                    details.Click+=(s,e)=>{BavarianUi.Feedback(details);bool open=expanded.Visibility!=ViewStates.Visible;expanded.Visibility=open?ViewStates.Visible:ViewStates.Gone;details.Text=open?T("Hide details","Скрыть подробности"):T("Details  ›","Подробнее  ›");};
                }
            }
            if(shown==0)TextLine(_cards,_planned.Length==0?T("Start a scan to see fault records.","Начните сканирование для просмотра ошибок."):T("No matching recognized records. Check module coverage above; this does not prove a fault-free vehicle.","Нет распознанных записей по фильтру. Проверьте охват блоков выше; это не доказывает отсутствие неисправностей."),14);
        }
    }
}
#endif
