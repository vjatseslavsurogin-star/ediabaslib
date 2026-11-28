#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Linq;
namespace BmwDeepObd
{
    internal sealed class BavarianScanSnapshot
    {
        public int Schema {get;set;}=1;
        public string Vin {get;set;}
        public string Time {get;set;}
        public ScanGroup[] Groups {get;set;}=Array.Empty<ScanGroup>();
        public bool Partial {get;set;}
        public sealed class ScanGroup
        {
            public string Module {get;set;}
            public string Variant {get;set;}
            public bool Read {get;set;}
            public string[] Codes {get;set;}=Array.Empty<string>();
        }
        internal static bool ValidVin(string vin)=>vin!=null&&vin.Length==17&&vin.All(c=>c>='0'&&c<='9'||c>='A'&&c<='Z'&&c!='I'&&c!='O'&&c!='Q');
        internal static string[] Compare(BavarianScanSnapshot a,BavarianScanSnapshot b,bool ru)
        {
            string T(string en,string r)=>ru?r:en;
            if(a==null||b==null||a.Schema!=1||b.Schema!=1||!ValidVin(a.Vin)||a.Vin!=b.Vin)
                throw new InvalidOperationException(T("Comparison requires the same verified VIN in both scans.","Для сравнения нужен одинаковый подтверждённый VIN обеих проверок."));
            if(a.Groups==null||b.Groups==null)throw new InvalidOperationException("Invalid snapshot groups");
            var lines=new List<string>{a.Vin,a.Time+" → "+b.Time,T("Recognized code records only; not a repair verdict. Groups can overlap.","Только распознанные коды; не заключение о ремонте. Группы могут пересекаться.")};
            var left=a.Groups.ToDictionary(g=>g.Module,StringComparer.Ordinal);var right=b.Groups.ToDictionary(g=>g.Module,StringComparer.Ordinal);
            foreach(string module in left.Keys.Union(right.Keys).OrderBy(x=>x,StringComparer.Ordinal))
            {
                left.TryGetValue(module,out var x);right.TryGetValue(module,out var y);
                if(x?.Read!=true||y?.Read!=true||string.IsNullOrEmpty(x.Variant)||x.Variant!=y.Variant)
                {
                    lines.Add(module+" · "+(x?.Read==true&&y?.Read!=true?T("No comparable later response","Поздний ответ отсутствует / не сопоставим"):T("Not comparable (coverage or variant)","Не сопоставимо (охват или вариант)")));continue;
                }
                var before=(x.Codes??Array.Empty<string>()).Where(c=>!string.IsNullOrWhiteSpace(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var after=(y.Codes??Array.Empty<string>()).Where(c=>!string.IsNullOrWhiteSpace(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string Show(IEnumerable<string> codes)=>string.Join(", ",codes.OrderBy(c=>c,StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("—"));
                lines.Add(module+" · "+x.Variant);
                lines.Add(T("  New records: ","  Новые записи: ")+Show(after.Except(before,StringComparer.OrdinalIgnoreCase)));
                lines.Add(T("  Still recorded: ","  Сохранились: ")+Show(after.Intersect(before,StringComparer.OrdinalIgnoreCase)));
                lines.Add(T("  Not seen in later recognized records: ","  Не обнаружены среди поздних записей: ")+Show(before.Except(after,StringComparer.OrdinalIgnoreCase)));
            }
            return lines.ToArray();
        }
    }
}
#endif
