#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.Globalization;
namespace BmwDeepObd
{
    // CBS_DATEN_LESEN metadata: d50m57e0 and komb60; CBSKENNUNG lookup.
    // Use explicit unit and dimension labels, never infer a missing unit or decode availability as wear.
    internal static class BavarianCbsPresentation
    {
        internal static string Label(string label,bool ru)=>label switch
        {
            "Motoroel"=>ru?"Моторное масло":"Engine oil",
            "Bremsbelag vorne"=>ru?"Передние тормозные колодки":"Front brake pads",
            "Bremsbelag hinten"=>ru?"Задние тормозные колодки":"Rear brake pads",
            "Bremsfluessigkeit"=>ru?"Тормозная жидкость":"Brake fluid",
            "Mikrofilter"=>ru?"Салонный фильтр":"Cabin filter",
            "Dieselpartikelfilter"=>ru?"Сажевый фильтр":"Diesel particulate filter",
            "Batterie"=>ru?"Аккумулятор":"Battery",
            "Sichtpruefung/Fahrzeug-Check"=>ru?"Проверка автомобиля":"Vehicle check",
            "§Fahrzeuguntersuchung"=>ru?"Технический осмотр (запись ECU)":"Vehicle inspection (ECU record)",
            "§Abgasuntersuchung"=>ru?"Проверка выхлопа (запись ECU)":"Emissions inspection (ECU record)",
            "Zuendkerzen" or "Zuendkerzen adaptiv"=>ru?"Свечи зажигания":"Spark plugs",
            "Kuehlfluessigkeit"=>ru?"Охлаждающая жидкость":"Coolant",
            _=>label
        };
        internal static string Value(IReadOnlyDictionary<string,string> row,bool ru)
        {
            string Get(string key)=>row.TryGetValue(key,out var v)?v?.Trim():null;
            bool Num(string key,out int n)=>int.TryParse(Get(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out n);
            var culture=CultureInfo.GetCultureInfo(ru?"ru-RU":"en-US");
            string dimension=Get("ST_UN_BOS_TEXT"),unit=Get("RMMI_BOS_EINH");
            var parts=new List<string>();
            // -32768 was observed in time-based records with valid target month/year.
            // Treat it conservatively as unavailable, not as overdue 32768 days.
            if(Num("RMMI_BOS_WERT",out int raw)&&raw!=-32768&&raw>=short.MinValue&&raw<short.MaxValue)
            {
                if(dimension=="Weg"&&(unit=="x 10 km"||unit=="km"))
                {
                    long km=(long)raw*(unit=="x 10 km"?10:1);
                    parts.Add((km<0?(ru?"Просрочено по ECU: ":"Overdue per ECU: "):(ru?"Остаток по ECU: ":"Remaining per ECU: "))+Math.Abs(km).ToString("N0",culture)+(ru?" км":" km"));
                }
                else if(dimension=="Zeit"&&unit=="Tage")
                    parts.Add((ru?"Дни по ECU: ":"ECU days: ")+raw.ToString(culture));
            }
            // Date fields have month/year semantics. Show numeric source year without inventing century
            // unless the field already contains a full four-digit year.
            if(dimension=="Zeit"&&Num("ZIEL_MM_WERT",out int month)&&month>=1&&month<=12&&Num("ZIEL_YY_WERT",out int year))
            {
                if(year>=2000&&year<=2099)parts.Add((ru?"Целевой месяц: ":"Target month: ")+month.ToString("00")+"/"+year);
                else if(year>=0&&year<=99)parts.Add((ru?"Целевой месяц/год ECU: ":"ECU target month/year: ")+month.ToString("00")+"/"+year.ToString("00"));
            }
            if(parts.Count==0)parts.Add(ru?"Интервал не определён":"Interval unavailable");
            if(Num("COU_RSTG_BOS_MESS_WERT",out int count)&&count>=0&&count<255)parts.Add((ru?"Счётчик обслуживания: ":"Service counter: ")+count);
            return string.Join("\n",parts);
        }
    }
}
#endif
