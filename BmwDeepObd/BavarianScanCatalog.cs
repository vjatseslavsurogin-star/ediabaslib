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
,
            ["d_szl.grp"]=new Target("SZL","7f4d88c032b350d4aad62f7dcf608aa901f75d1e2781154e789d73a09fc1286b",new Dictionary<string,string>(StringComparer.Ordinal){["szl.prg"]="cd1d7d8359feec8c279e586aad1133b867d1102594adf0a04a3ba0b57ef4f3a4",["szl_60.prg"]="bb6bdf1df9284e24d763da0f6412b5f878d6b823116653f1213693fded411c71"}),
            ["d_afs.grp"]=new Target("AFS","174281dacd9cecc7ca2ee8f5fc7557cee58b14d03780ce6a515357a9542a5569",new Dictionary<string,string>(StringComparer.Ordinal){["afs_60.prg"]="52368d791e617a154c82d0c9bc81dd413cfbc4b5134e63d05717ff584f6be8d2"}),
            ["d_ars.grp"]=new Target("ARS / Dynamic Drive","cc1a46523c744175a4012eb82359d0e268857c75c417ce910ceff4fd9c5f9b2b",new Dictionary<string,string>(StringComparer.Ordinal){["ars_60.prg"]="d938f4950b42f531ed69e2702e739c4cbd9ad669b629bc4788ef0500ca13118c"}),
            ["d_ekp.grp"]=new Target("EKP","2823055b94b52c09a86289ca684779b65d2149bb807f50f949cd71f8040b135a",new Dictionary<string,string>(StringComparer.Ordinal){["ekp_60.prg"]="0e737360f5a156df0ff1816f674680c048e6efe9a8a8608e73076bfba175e83b",["ekpm60_2.prg"]="e284592e56507c21dfcc101332201940f45832c85e2c26cbd7c1a055bc801b3e",["ekpm60_3.prg"]="f33b5c21eea51e4d01a20b0d2cde60d68873751c8872fdcac4cd244112154f09",["ekp360.prg"]="1289c87799a7a3987d8c6419819bc394347c2c65cc4205ded025da880cf7f8be"}),
            ["d_rdc.grp"]=new Target("RDC","28de25e33bd3d28cbad8a034636c23c0a316af3a9d6e2e3616066e08a523b809",new Dictionary<string,string>(StringComparer.Ordinal){["rdc_60.prg"]="6e5199574d6e99a6e4723f9fdcaf989a26e973638bc828425c4e1c0c7cb0adf5",["rdc_can.prg"]="72ff7edf68ba72ff218f8a79d58889a5d558a974019b83fefc8810fa023ebfcb"}),
            ["d_amp.grp"]=new Target("AMP","c23c567528e5c69f913be95b3332923d2ff82d279bd949b732b5760b3fc74609",new Dictionary<string,string>(StringComparer.Ordinal){["amp_60.prg"]="20ef5a658da7b33d2f3803942e7ad55ef0a71101463e269b9d99d7f3c2628505"}),
            ["d_hud.grp"]=new Target("HUD","202ad4bc43ad584a43bac2753722d4857e735a36c09fcc0ad0c549eb7cb8252d",new Dictionary<string,string>(StringComparer.Ordinal){["hud_60.prg"]="5d2af94d0c35189e5ced3aae5fbd2c8e459dd291fc979263c8e7f6794121d93d"}),
            ["d_shd.grp"]=new Target("SHD","070470502f309050018d9a2f92d7f20a0f334e4e8c67e7e1def85c19435fefbe",new Dictionary<string,string>(StringComparer.Ordinal){["sd_kwp.prg"]="c617a3aa968e634773034f5b986351bcc4d0e055ff74fccd174c032b844a5fd2"}),
            ["d_kbm.grp"]=new Target("KBM","09ca4085011f87b70695120dcaad817c6deb31c7ecf7ccf4c4365e8b3166440b",new Dictionary<string,string>(StringComparer.Ordinal){["kbm_60.prg"]="4b393ac2df3612929d53746d2869e5ca37d573ba0da54e636fca4165a4a5e288"}),
            ["d_mmi.grp"]=new Target("MMI / CCC / M-ASK","55f2badccd8d91b015b8adab0c1d02c4473dd1794cd45c748c6006fd04c6f298",new Dictionary<string,string>(StringComparer.Ordinal){["ccc_60.prg"]="ca499feefaf742b7fc315606a3f49c38e15765ee383ca88aa29d916a6234c741",["mask60.prg"]="baa857e1529ada0aa83c00ef74807d4c421419adb6d589e4abff8934cb60090b"}),
            ["d_mostgw.grp"]=new Target("MOST gateway","b08815dfcffc02589ec28b3acd3183bb83f8caf83fb846e2df75d203b17962d5",new Dictionary<string,string>(StringComparer.Ordinal){["mcgw60.prg"]="6c3fe1c3f33b493794d6c862327c495e5a01decb7baca38ee3a3f9dc335ca2ea",["cccg60.prg"]="4c4d1992719b3229206a89c37d8dd4278891269ed421ddf6f2a46ba8e88c5bc4"}),
            ["d_cid.grp"]=new Target("CID","dc70970204b6e723d76db4be78583432497d4a77c903b5edea2ef4523e8d9e2a",new Dictionary<string,string>(StringComparer.Ordinal){["cid_60.prg"]="3315290600f341fc7178c15ec6652a83667e8855443a2dfcd523c51e1373e5b6"}),
            ["d_pow.grp"]=new Target("MPM","3dea9a2b5c53799eeb3477e0c8974d51d5f0c1d9b2f044195fc4d35753ccb727",new Dictionary<string,string>(StringComparer.Ordinal){["mpm_60.prg"]="344ee4277b2fd59e8c619406e00b90a610744aaa59ec6323349ee5fbde4fa3cf"}),
            ["d_tel.grp"]=new Target("TCU / MULF","ba50f415f87ee63cb13bec3c856b35acfd76bc2693ab5717989c15d20ad7d720",new Dictionary<string,string>(StringComparer.Ordinal){["tele60_2.prg"]="462a6dd77c10646d8efea0055cca7182052bb8ca7ca24631cac5e28922f71273",["tele60_3.prg"]="0959c63aec75138a9e2048cf6699a2b697123b2acb7db6b41e25314ea0125c7d",["ulf_60.prg"]="7aef633fcf9f08d6e5cba4afeba235a09169bf5a47dba64fdba6444308a18724",["ulf_60pb.prg"]="6647135cf6c638f243b14c4b7233b0a7e76a10db12b6668b60dcde4cb5b70d6e",["ulf2_60.prg"]="b31b85127fb3f3592335c0d31ddd174c62f11c662376331f6e790fd04d828086"})
        };
        internal static readonly string[] AllGroups={"d_motor.grp","d_egs.grp","d_cas.grp","d_dsc.grp","d_kombi.grp","d_klima.grp","d_pdc.grp","d_lm.grp","d_ccc.grp","d_sim.grp"};
        // Catalog candidates, not an installed ECU count. The old 10-group scan is retained separately.
        internal static readonly string[] DiscoveryGroups={"d_cas.grp","d_motor.grp","d_egs.grp","d_dsc.grp","d_kombi.grp","d_klima.grp","d_pdc.grp","d_lm.grp","d_ccc.grp","d_sim.grp","d_szl.grp","d_afs.grp","d_ars.grp","d_ekp.grp","d_rdc.grp","d_amp.grp","d_hud.grp","d_shd.grp","d_kbm.grp","d_mmi.grp","d_mostgw.grp","d_cid.grp","d_pow.grp","d_tel.grp"};
        internal static string Module(string group)=>group=="d_motor.grp"?"DDE":group=="d_egs.grp"?"EGS":Extra[group].Name;
        internal static bool IsExtended(string group)=>Extra.ContainsKey(group);
        internal static bool TryGroupHash(string group,out string hash){hash=null;if(!Extra.TryGetValue(group,out var target))return false;hash=target.Hash;return true;}
        internal static bool TryVariantHash(string group,string variant,out string hash){hash=null;return Extra.TryGetValue(group,out var target)&&target.Variants.TryGetValue(variant,out hash);}
    }
}
#endif
