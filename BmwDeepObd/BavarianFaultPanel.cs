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
            public string Module,Variant,Error;
            public List<Dictionary<string,string>> Records;
        }
        private readonly bool _ru;
        private readonly List<ModuleResult> _results=new List<ModuleResult>();
        private readonly CheckBox _all;
        private readonly LinearLayout _cards;
        private readonly TextView _summary;
        private string T(string en,string ru)=>_ru?ru:en;
        private int Dp(int n)=>(int)(n*Resources.DisplayMetrics.Density+0.5f);
        public BavarianFaultPanel(Context context,bool ru):base(context)
        {
            _ru=ru;Orientation=Orientation.Vertical;
            _summary=TextLine(this,T("No scan performed.","Чтение ещё не выполнено."),16);
            _all=new CheckBox(context){Text=T("Show all records (including not occurred)","Все записи (включая не возникавшие)")};
            AddView(_all);
            _cards=new LinearLayout(context){Orientation=Orientation.Vertical};AddView(_cards);
            _all.CheckedChange+=(s,e)=>Render();
        }
        private TextView TextLine(LinearLayout parent,string text,int size)
        {
            var t=new TextView(Context){Text=text,TextSize=size};t.SetTextIsSelectable(true);parent.AddView(t);return t;
        }
        private static string Get(Dictionary<string,string> r,string k)=>r.TryGetValue(k,out string value)?value??"":"";
        public void BeginScan(){_results.Clear();Render();}
        public void AddFailure(string module,string error)
        {_results.Add(new ModuleResult{Module=module,Error=error});Render();}
        public void AddResult(string module,string variant,List<Dictionary<string,string>> rows)
        {
            var records=new List<Dictionary<string,string>>();
            Dictionary<string,string> current=null;
            foreach(var r in rows??new List<Dictionary<string,string>>())
            {
                if(r.ContainsKey("F_HEX_CODE")||(r.ContainsKey("F_ORT_NR")&&(current==null||current.ContainsKey("F_ORT_NR"))))
                {
                    if(current!=null)records.Add(current);
                    current=new Dictionary<string,string>(r);
                }
                else if(current!=null)
                    foreach(var kv in r)if(kv.Key.StartsWith("F_",StringComparison.Ordinal))current[kv.Key]=kv.Value;
            }
            if(current!=null)records.Add(current);
            _results.Add(new ModuleResult{Module=module,Variant=variant,Records=records});Render();
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
        private string Status(string value)=>value switch {
            "35"=>T("ACTIVE / STORED","АКТИВНАЯ / СОХРАНЕНА"),
            "33"=>T("STORED / NOT CURRENT","СОХРАНЕНА / НЕ АКТИВНА"),
            "32"=>T("NOT OCCURRED","НЕ ВОЗНИКАЛА"),
            _=>T("UNKNOWN STATUS","НЕИЗВЕСТНЫЙ СТАТУС")};
        private void Render()
        {
            _cards.RemoveAllViews();
            var records=_results.Where(m=>m.Records!=null).SelectMany(m=>m.Records).ToList();
            _summary.Text=T("Active: ","Активные: ")+records.Count(f=>Get(f,"F_VORHANDEN_NR")=="35")+
                T(" · Stored: "," · Сохранённые: ")+records.Count(f=>Get(f,"F_VORHANDEN_NR")=="33")+
                T(" · Other: "," · Остальные: ")+records.Count(f=>Get(f,"F_VORHANDEN_NR")!="35"&&Get(f,"F_VORHANDEN_NR")!="33")+
                "\n"+T("Modules read: ","Блоков прочитано: ")+_results.Count(m=>m.Records!=null)+"/2";
            foreach(var m in _results)
            {
                TextLine(_cards,m.Module+(m.Variant==null?"":" · "+m.Variant),18);
                if(m.Error!=null){TextLine(_cards,T("Read failed: ","Не прочитан: ")+m.Error,14);continue;}
                if(m.Records.Count==0)
                {TextLine(_cards,T("No recognizable records; check raw report.","Распознанных записей нет; проверьте исходный отчёт."),14);continue;}
                var visible=m.Records.Where(f=>_all.Checked||Get(f,"F_VORHANDEN_NR")!="32")
                    .OrderBy(f=>Get(f,"F_VORHANDEN_NR")=="35"?0:Get(f,"F_VORHANDEN_NR")=="33"?1:2).ToList();
                if(visible.Count==0)TextLine(_cards,T("No active, stored or unknown-status records in this response.","Нет активных, сохранённых или неопределённых записей в этом ответе."),14);
                foreach(var f in visible)
                {
                    string code=Code(f),state=Get(f,"F_VORHANDEN_NR");
                    var card=new LinearLayout(Context){Orientation=Orientation.Vertical};
                    card.SetPadding(Dp(12),Dp(10),Dp(12),Dp(10));card.SetBackgroundColor(Color.Rgb(22,32,49));
                    var lp=new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,ViewGroup.LayoutParams.WrapContent){TopMargin=Dp(8)};
                    _cards.AddView(card,lp);
                    var title=TextLine(card,m.Module+" · "+code,20);title.SetTextColor(Color.White);
                    var status=TextLine(card,Status(state),13);
                    status.SetTextColor(state=="35"?Color.Rgb(255,111,111):state=="33"?Color.Rgb(255,190,92):Color.Rgb(180,192,209));
                    TextLine(card,Description(m,code,f),16).SetTextColor(Color.White);
                    if(Get(f,"F_WARNUNG_NR")=="49")
                        TextLine(card,T("May request a warning lamp; actual MIL state not measured.","Может вызвать контрольную лампу; фактическое состояние MIL не измерено."),13).SetTextColor(Color.Rgb(255,190,92));
                    var details=new Button(Context){Text=T("Technical details","Технические подробности")};card.AddView(details);
                    string original=string.Join("\n",f.Select(k=>k.Key+": "+k.Value));
                    details.Click+=(s,e)=>new AndroidX.AppCompat.App.AlertDialog.Builder(Context).SetTitle(m.Module+" · "+code)
                        .SetMessage(original).SetPositiveButton("OK",(d,a)=>{}).Show();
                }
            }
        }
    }
}
#endif
