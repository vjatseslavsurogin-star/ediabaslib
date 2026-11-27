#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
namespace BmwDeepObd
{
    internal static class BavarianScanCatalog
    {
        private sealed class Target
        {
            public readonly string Name,Hash;
            public readonly Dictionary<string,string> Variants;
            public Target(string name,string hash,Dictionary<string,string> variants){Name=name;Hash=hash;Variants=variants;}
        }
        private static readonly Dictionary<string,Target> Extra=new Dictionary<string,Target>(StringComparer.Ordinal)
        {
            ["d_cas.grp"]=new Target("CAS","3d4f0bd7113607e64ea8ed84b40771cc29d9b331804345c265f453e939755559",new Dictionary<string,string>(StringComparer.Ordinal){["cas.prg"]="76715fff3d5dab73967afe85482852d960ce9dba914986e9b2fbf22c6214b052"}),
            ["d_dsc.grp"]=new Target("DSC","ba74a694733a0eb09cf6dc49a3e051b462817c823f660e2e75b0a9b502be1a70",new Dictionary<string,string>(StringComparer.Ordinal){["dsc_60.prg"]="9309c1ea87d958c7d4c3e192be94db3df246fb266e2283ee1b542d43a0370b6a",["dsc_60pp.prg"]="7dfdf13286136fdead9ace37a4787a8aaa6719b63f2820e00e8474c7a78190de",["dxc8_p.prg"]="19b5970f06029606285d7f8cf86d36e6f91fdc5c5d13b99403dadfaea5024a42"}),
            ["d_kombi.grp"]=new Target("KOMBI","08e360b7ff8c909ec6be2f8573c5622c5e2acf06d6a250e6ad9bf806de8ac44d",new Dictionary<string,string>(StringComparer.Ordinal){["komb60.prg"]="cc26a2ce46aea61a5e23d36fa36632aa1eb39e46f21292bddc5cf1b4687d5df3"}),
            ["d_klima.grp"]=new Target("IHKA","a3c794cfa503f377082c617d421862ee7ea95a7b13620e3830c8b9df57420f0c",new Dictionary<string,string>(StringComparer.Ordinal){["ihka60.prg"]="7ae83ee1c3c2cf4419827039386d207562c94141cdf6ac4f7c7a073b0f5ba10a",["ihka60_2.prg"]="4ae94d702e0ab4846f887e651450f20f483573eed494d107421d4f33a3f488f1"}),
            ["d_pdc.grp"]=new Target("PDC","0441758c7eaa6cb8c65d4c2e1d09d0b976a1b8abb8dfc1885f8949243d23d6b8",new Dictionary<string,string>(StringComparer.Ordinal){["pdc_65_2.prg"]="7ef8229bc87e6c1814d83ab8090a934e840ec4686954ab73ac044b529f9d2418",["pdc_e65.prg"]="7451b42bb2ccd2b8babe3d37cbf1fa74be8360aaf36f09f0af88ecb62c9d62d0"}),
            ["d_lm.grp"]=new Target("LM","b1c59eb697c5046e47162f420d23985bda752a8ede813d2f48ebb01ffe0a3af8",new Dictionary<string,string>(StringComparer.Ordinal){["lm_60.prg"]="2a840568a8158cbdc212ca3d420787e1ca6e37fbb014160af1a623615cace36a",["lm_ahl.prg"]="430f52043eb3aebeda28a6f6ff53a44887454a0a1a2691a7d618e43ddaffd8d6",["lm_ahl_2.prg"]="b818ec3d9e604a65fd62270c451e4c928f80719e6f03e4b8b61a57e08874519e"}),
            ["d_ccc.grp"]=new Target("CCC","b1dc7081a3cc168cd7b70093187d6f48bb6e8b332943eda02a2b63d78f825236",new Dictionary<string,string>(StringComparer.Ordinal){["ccc_60.prg"]="ca499feefaf742b7fc315606a3f49c38e15765ee383ca88aa29d916a6234c741",["ccca60.prg"]="8d20fb5002a33b95957fc2ee2f9811cad0905d277929edd200a2164b2d1686d1"}),
            ["d_sim.grp"]=new Target("SGM / ACSM","73d9056040844e3c97b98a97ce943da83f2758e6349ca669fbc5ff416ec532cd",new Dictionary<string,string>(StringComparer.Ordinal){["sgm_60_2.prg"]="15c9d78563d418aa2e6c7171087ebef85cd067dfcf673072a6b2d7b998b1dcb4",["acsm60.prg"]="2f77e61c7067b54235496eacc8f401e91155958a6ae7bab0b9ae1923ecd90782"})
        };
        internal static readonly string[] AllGroups={"d_motor.grp","d_egs.grp","d_cas.grp","d_dsc.grp","d_kombi.grp","d_klima.grp","d_pdc.grp","d_lm.grp","d_ccc.grp","d_sim.grp"};
        internal static string Module(string group)=>group=="d_motor.grp"?"DDE":group=="d_egs.grp"?"EGS":Extra[group].Name;
        internal static bool IsExtended(string group)=>Extra.ContainsKey(group);
        internal static bool TryGroupHash(string group,out string hash){hash=null;if(!Extra.TryGetValue(group,out var target))return false;hash=target.Hash;return true;}
        internal static bool TryVariantHash(string group,string variant,out string hash){hash=null;return Extra.TryGetValue(group,out var target)&&target.Variants.TryGetValue(variant,out hash);}
    }
}
#endif
