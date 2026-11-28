#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
namespace BmwDeepObd
{
    internal static class BavarianFaultRecords
    {
        internal static string Get(Dictionary<string,string> r,string k)=>r.TryGetValue(k,out var v)?v??"":"";
        internal static string Code(Dictionary<string,string> r)
        {
            if(int.TryParse(Get(r,"F_ORT_NR"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)&&n>0&&n<=0xFFFFFF)return n.ToString("X4");
            string raw=Get(r,"F_HEX_CODE").Trim();
            if(Get(r,"F_VERSION")=="2"&&raw.Length==6&&raw.All(Uri.IsHexDigit))raw=raw.Substring(0,4);
            return raw.Length==4&&raw.All(Uri.IsHexDigit)&&raw!="0000"?raw.ToUpperInvariant():"";
        }
        internal static string State(Dictionary<string,string> r)
        {
            if(Get(r,"F_VERSION")!="2")return "";
            string state=Get(r,"F_VORHANDEN_NR");
            return state=="35"||state=="33"||state=="32"?state:"";
        }
        internal static List<Dictionary<string,string>> Parse(List<Dictionary<string,string>> rows)
        {
            var records=new List<Dictionary<string,string>>();Dictionary<string,string> current=null;string version="";
            foreach(var r in rows??new())
            {
                if(r==null)continue;
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
        // A successful job and a usable memory layout are different facts.
        // An empty recognized set is not proof of no DTCs.
        internal static bool Comparable(List<Dictionary<string,string>> rows,List<Dictionary<string,string>> records)
        {
            if(rows==null||records==null)return false;
            if(records.Count>0)return records.All(r=>Code(r)!=""&&State(r)!="");
            // Do not assume a status-only response is an explicit empty fault memory.
            return false;
        }
        internal static int CountRelevant(IEnumerable<Dictionary<string,string>> records)=>records.Count(r=>Code(r)!=""&&State(r)!="32");
    }
}
#endif
