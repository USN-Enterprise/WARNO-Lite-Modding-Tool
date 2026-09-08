using System.Text.RegularExpressions;
namespace WarnoLiteModdingTool.App.Localisation;
public static class DivisionNames
{
    private static readonly Dictionary<string,string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BEL_16e_Mecanisee"] = "比利时 第16机械化师",
        ["BEL_Division_Mobilisation"] = "比利时 动员师",
        ["CAN_1st_Canadian"] = "加拿大 第1师",
        ["DK_Jyske"] = "丹麦 日德兰师",
        ["DK_Ostre_Landkommando"] = "丹麦 东部陆军司令部",
        ["ESP_Division_Brunete"] = "西班牙 布鲁内特装甲师",
        ["ESP_Division_Guzman"] = "西班牙 古斯曼机械化师",
        ["FR_11e_Para"] = "法国 第11伞兵师",
        ["FR_152e_Infanterie"] = "法国 第152步兵师",
        ["FR_5e_Blindee"] = "法国 第5装甲师",
        ["FR_6e_Blindee_Legere"] = "法国 第6轻装甲师",
        ["FR_Division_du_Rhin"] = "法国 莱茵师",
        ["FR_ZdD_Paris"] = "法国 巴黎防区",
        ["NATO_Garnison_Berlin"] = "北约 柏林驻军",
        ["NL_4e_Divisie"] = "荷兰 第4师",
        ["NL_CLKA"] = "荷兰 陆军军团后勤司令部",
        ["POL_15_Zmechanizowana"] = "波兰 第15机械化师",
        ["POL_20_Pancerna"] = "波兰 第20装甲师",
        ["POL_4_Zmechanizowana"] = "波兰 第4机械化师",
        ["POL_Korpus_Desantowy"] = "波兰 登陆军",
        ["RDA_20_MSD"] = "东德 第20摩托化步兵师",
        ["RDA_4_MSD"] = "东德 第4摩托化步兵师",
        ["RDA_7_Panzer"] = "东德 第7装甲师",
        ["RDA_9_Panzer"] = "东德 第9装甲师",
        ["RDA_KdA_Bezirk_Erfurt"] = "东德 埃尔福特区工人战斗队",
        ["RDA_Rugen_Gruppierung"] = "东德 吕根集群",
        ["RFA_1_Luftlande"] = "西德 第1空降师",
        ["RFA_2_PzGrenadier"] = "西德 第2装甲掷弹兵师",
        ["RFA_5_Panzer"] = "西德 第5装甲师",
        ["RFA_6_PzGrenadier"] = "西德 第6装甲掷弹兵师",
        ["RFA_TerrKdo_Sud"] = "西德 南部国土防卫司令部",
        ["RFA_VTK_42"] = "西德 第42国土防卫司令部",
        ["SOV_119IndTkBrig"] = "苏联 第119独立坦克旅",
        ["SOV_157_Rifle"] = "苏联 第157摩托化步兵师",
        ["SOV_17_Gds_Tank"] = "苏联 近卫第17坦克师",
        ["SOV_1_Gds_Rifle"] = "苏联 近卫第1摩托化步兵师",
        ["SOV_25_Tank"] = "苏联 第25坦克师",
        ["SOV_27_Gds_Rifle"] = "苏联 近卫第27摩托化步兵师",
        ["SOV_2_Gds_Rifle"] = "苏联 近卫第2摩托化步兵师",
        ["SOV_31_Tank"] = "苏联 第31坦克师",
        ["SOV_336_Naval_Brigade"] = "苏联 第336海军步兵旅",
        ["SOV_35_AirAslt_Brig"] = "苏联 第35空中突击旅",
        ["SOV_39_Gds_Rifle"] = "苏联 近卫第39摩托化步兵师",
        ["SOV_56_AirAslt_Brig"] = "苏联 第56空中突击旅",
        ["SOV_57_GMRD_challenge_Sledgehammer"] = "苏联 近卫第57摩托化步兵师〔剧情〕",
        ["SOV_6IndMSBrig"] = "苏联 第6独立摩托化步兵旅",
        ["SOV_71_Tank"] = "苏联 第71坦克师",
        ["SOV_76_VDV"] = "苏联 第76空降师",
        ["SOV_79_Gds_Tank"] = "苏联 近卫第79坦克师",
        ["SOV_79_Gds_Tank_HB"] = "苏联 近卫第79坦克师〔剧情〕",
        ["SOV_94_Gds_Rifle"] = "苏联 近卫第94摩托化步兵师",
        ["SOV_DDR_Special_Division"] = "苏联 驻东德特种编队",
        ["SOV_DON_100"] = "苏联 第100特种用途摩托化师",
        ["TCH_19_MSD"] = "捷克斯洛伐克 第19摩托化步兵师",
        ["TCH_1_Tank"] = "捷克斯洛伐克 第1坦克师",
        ["TCH_2_MSD"] = "捷克斯洛伐克 第2摩托化步兵师",
        ["TCH_303_Tank"] = "捷克斯洛伐克 第303坦克师",
        ["UK_1st_Armoured"] = "英国 第1装甲师",
        ["UK_2nd_Infantry"] = "英国 第2步兵师",
        ["UK_4th_Armoured"] = "英国 第4装甲师",
        ["UK_5th_Airborne_Brigade"] = "英国 第5空降旅",
        ["UK_London_HDR"] = "英国 伦敦本土防区",
        ["US_101st_Airmobile"] = "美国 第101空中突击师",
        ["US_11ACR"] = "美国 第11装甲骑兵团",
        ["US_11ACR_HB"] = "美国 第11装甲骑兵团〔剧情〕",
        ["US_1st_Cavalry"] = "美国 第1骑兵师",
        ["US_24th_Inf"] = "美国 第24步兵师",
        ["US_2nd_Marine"] = "美国 第2海军陆战师",
        ["US_35th_Inf"] = "美国 第35步兵师",
        ["US_3rd_Arm"] = "美国 第3装甲师",
        ["US_6th_Light"] = "美国 第6轻步兵师",
        ["US_82nd_Airborne"] = "美国 第82空降师",
        ["US_8th_Inf"] = "美国 第8步兵师",
        ["US_9th_Mot"] = "美国 第9摩托化步兵师",
        ["WP_Unternehmen_Zentrum"] = "华约 中央行动集群",
    };
    public static string Display(string raw, bool english)
    {
        if (english) return raw;
        var key = raw.Replace("Descriptor_Deck_Division_", "").Replace(' ', '_');
        if (key.EndsWith("_Rule")) key = key[..^5];
        var story = !key.Contains("_multi") || key.Contains("_HB") || key.Contains("_challenge");
        key = key.Replace("_multi", "");
        if (Names.TryGetValue(key, out var name))
            return story && !name.Contains("〔剧情〕") ? name + "〔剧情〕" : name;
        return raw.Replace(" multi", "").Replace("_multi", "").Replace(" Rule", "").Replace("_Rule", "");
    }
}
