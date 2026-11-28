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
