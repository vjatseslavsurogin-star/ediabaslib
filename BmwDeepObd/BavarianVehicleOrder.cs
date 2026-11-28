#if BAVARIAN_USB_BASELINE
using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace BmwDeepObd
{
    internal static class BavarianVehicleOrder
    {
        // Text format used by upstream VehicleOrder.ToString(): BR#date*type%paint&trim$SA-Eword+HOword.
        // This is NOT a decoder for binary or hex-encoded CAS C_FA_LESEN data.
        private static readonly Regex TextFormat=new Regex(
            @"\A(?<series>E[0-9]{2})#(?<date>[0-9]{4})\*(?<type>[A-Z0-9]{4})%(?<paint>[A-Z0-9]{3,4})&(?<trim>[A-Z0-9]{4})(?<sa>(?:\$[A-Z0-9]{3})*)(?<ew>(?:-[A-Z0-9]{1,8})*)(?<ho>(?:\+[A-Z0-9]{1,8})*)\z",
            RegexOptions.CultureInvariant);
        internal sealed record Order(string Series,string Date,string Type,string Paint,string Trim,string[] Options);
        internal sealed class DecodedOrder
        {
            public DecodedOrder(){}
            public string Series {get;set;}
            public string Date {get;set;}
            public string Type {get;set;}
            public string Paint {get;set;}
            public string Trim {get;set;}
            public string StandardFa {get;set;}
            public string[] Options {get;set;}=Array.Empty<string>();
            public string[] OtherWords {get;set;}=Array.Empty<string>();
        }
        // Only accept an explicit hex representation. Do not reinterpret arbitrary text as bytes.
        internal static byte[] HexStream(string raw)
        {
            if(string.IsNullOrEmpty(raw)||raw.Length>640||raw.Length%2!=0||!raw.All(Uri.IsHexDigit))return null;
            return Convert.FromHexString(raw);
        }
        internal static DecodedOrder ParseDecoded(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,string>> rows)
        {
            if(rows==null||rows.Count==0)return null;
            var values=rows.SelectMany(r=>r).GroupBy(k=>k.Key,StringComparer.Ordinal)
                .ToDictionary(g=>g.Key,g=>g.Select(k=>k.Value).Distinct(StringComparer.Ordinal).ToArray(),StringComparer.Ordinal);
            string Field(string k)=>values.TryGetValue(k,out var v)&&v.Length==1?v[0]:null;
            if(Field("JOB_STATUS")!="OKAY"||!int.TryParse(Field("SA_ANZ"),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out int count)||count<0||count>256)return null;
            var entries=values.Where(k=>Regex.IsMatch(k.Key,@"\ASA_[0-9]+\z",RegexOptions.CultureInvariant)).Select(k=>k.Value).ToArray();
            if(entries.Length!=count||entries.Any(v=>v.Length!=1||v[0]==null||!Regex.IsMatch(v[0].Trim(),@"\A[A-Z0-9]{3}\z",RegexOptions.CultureInvariant)))return null;
            var options=entries.Select(v=>v[0].Trim()).ToArray();
            if(options.Distinct(StringComparer.Ordinal).Count()!=count)return null;
            string standard=Field("STANDARD_FA"),series=Field("BR"),date=Field("C_DATE"),type=Field("C_TYP");
            if(string.IsNullOrWhiteSpace(standard)||standard.Length>2048||string.IsNullOrWhiteSpace(series)||string.IsNullOrWhiteSpace(type))return null;
            return new DecodedOrder{Series=series,Date=date,Type=type,Paint=Field("LACK"),Trim=Field("POLSTER"),StandardFa=standard,Options=options.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),
                OtherWords=values.Where(k=>Regex.IsMatch(k.Key,@"\A(?:HO_WORT|E_WORT|ZUSBAU)_[0-9]+\z",RegexOptions.CultureInvariant)&&k.Value.Length==1).Select(k=>k.Key+": "+k.Value[0]).ToArray()};
        }
        internal static Order ParseText(string raw)
        {
            if(string.IsNullOrWhiteSpace(raw)||raw.Length>2048)return null;
            var m=TextFormat.Match(raw.Trim());
            if(!m.Success)return null;
            string date=m.Groups["date"].Value;
            if(!int.TryParse(date.Substring(0,2),out var month)||month<1||month>12)return null;
            var options=m.Groups["sa"].Value.Split('$',StringSplitOptions.RemoveEmptyEntries);
            if(options.Distinct(StringComparer.Ordinal).Count()!=options.Length)return null;
            return new Order(m.Groups["series"].Value,date,m.Groups["type"].Value,m.Groups["paint"].Value,m.Groups["trim"].Value,options);
        }
    }
}
#endif
