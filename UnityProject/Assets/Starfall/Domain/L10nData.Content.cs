namespace Starfall.Domain
{
    internal static partial class L10nData
    {
        // GameContentCatalog names/descriptions and the closed UniverseGenerator
        // vocabulary used to transliterate generated proper nouns.
        private static readonly (string en, string zh)[] ContentEntries =
        {
            // --- Factions ---
            ("Aurelian Empire", "奥雷利安帝国"),
            ("A theocratic golden empire, master of laser weaponry.", "政教合一的黄金帝国，激光武器的大师。"),
            ("Kaldari State", "卡尔达里合众国"),
            ("A corporate state built on missiles and railguns.", "建立在导弹与磁轨炮之上的企业国家。"),
            ("Meridian Federation", "子午线联邦"),
            ("A free federation favoring blasters and drones.", "偏爱疾炮与无人机的自由联邦。"),
            ("Varkhald Republic", "瓦克哈德共和国"),
            ("A rugged republic of projectile-weapon clans.", "由弹道武器氏族组成的坚韧共和国。"),
            ("Blood Reavers", "血掠者"),
            ("Fanatical raiders bleeding the Aurelian frontier.", "肆虐奥雷利安边境的狂热掠袭者。"),
            ("Nathari Hive", "纳萨里蜂巢"),
            ("A cybernetic hive mind infesting Kaldari space.", "盘踞卡尔达里宇域的机械蜂群意识。"),
            ("Crimson Hand", "绯红之手"),
            ("Smugglers and cartel enforcers of the Federation.", "联邦境内的走私者与卡特尔执法者。"),
            ("Ashfang Cartel", "灰牙卡特尔"),
            ("Nomad cartel preying on the Varkhald Republic.", "袭掠瓦克哈德共和国的游牧卡特尔。"),
            ("Sisters of the Veil", "面纱修会"),
            ("A humanitarian order devoted to exploration and mercy.", "致力于探索与济世的人道教团。"),
            ("The Directorate", "理事会"),
            ("The unified stellar authority policing all of known space.", "执掌全部已知空域治安的统一星际权威。"),

            // --- Ships ---
            ("Acolyte", "侍从"),
            ("Fast Aurelian laser frigate.", "奥雷利安高速激光护卫舰。"),
            ("Templar", "圣殿骑士"),
            ("Aurelian destroyer with heavy pulse lasers.", "装备重型脉冲激光的奥雷利安驱逐舰。"),
            ("Dawnbringer", "拂晓使者"),
            ("Aurelian cruiser, a floating battery of light.", "奥雷利安巡洋舰，一座漂浮的光之炮台。"),
            ("Justicar", "裁决者"),
            ("Aurelian battlecruiser, judgment in a hull.", "奥雷利安战列巡洋舰，航行于星海的裁决。"),
            ("Seraph", "炽天使"),
            ("Golden Aurelian battleship of judgment.", "奥雷利安黄金审判战列舰。"),
            ("Shrike", "伯劳鸟"),
            ("Kaldari missile frigate, long reach.", "卡尔达里导弹护卫舰，打击距离悠长。"),
            ("Heron", "苍鹭"),
            ("Kaldari railgun destroyer.", "卡尔达里磁轨炮驱逐舰。"),
            ("Rook", "寒鸦"),
            ("Kaldari missile cruiser with deep shields.", "卡尔达里导弹巡洋舰，护盾深厚。"),
            ("Warden", "守望者"),
            ("Kaldari battlecruiser, a mobile shield fortress.", "卡尔达里战列巡洋舰，一座机动的护盾堡垒。"),
            ("Onyx", "缟玛瑙"),
            ("Kaldari battleship, a fortress of shields.", "卡尔达里战列舰，一座护盾堡垒。"),
            ("Wasp", "黄蜂"),
            ("Meridian blaster frigate, fast and mean.", "子午线疾炮护卫舰，迅捷而凶悍。"),
            ("Anvil", "铁砧"),
            ("Meridian destroyer built for brawls.", "为近身缠斗而生的子午线驱逐舰。"),
            ("Mantis", "螳螂"),
            ("Meridian cruiser with crushing close-range damage.", "子午线巡洋舰，近战伤害极具毁灭性。"),
            ("Bulwark", "壁垒"),
            ("Meridian battlecruiser that anchors the line.", "子午线战列巡洋舰，阵线的中流砥柱。"),
            ("Colossus", "巨像"),
            ("Meridian battleship, an armored giant.", "子午线战列舰，一尊装甲巨人。"),
            ("Fang", "獠牙"),
            ("Varkhald autocannon frigate, fastest hull afloat.", "瓦克哈德自动炮护卫舰，现役最快船体。"),
            ("Maul", "重锤"),
            ("Varkhald destroyer with relentless barrage.", "瓦克哈德驱逐舰，弹幕绵延不绝。"),
            ("Broadsword", "阔剑"),
            ("Varkhald cruiser, balanced and brutal.", "瓦克哈德巡洋舰，均衡而凶悍。"),
            ("Warhound", "战獒"),
            ("Varkhald battlecruiser that hunts in packs.", "瓦克哈德战列巡洋舰，成群猎杀。"),
            ("Stormcaller", "风暴召唤者"),
            ("Varkhald battleship that brings the storm.", "瓦克哈德战列舰，携风暴而至。"),
            ("Pilgrim", "朝圣者"),
            ("Sisters of the Veil exploration frigate.", "面纱修会探索护卫舰。"),
            ("Enforcer", "执法者"),
            ("Directorate response cruiser. Not for sale.", "理事会快速反应巡洋舰。非卖品。"),

            // --- Modules ---
            ("Pulse Laser", "脉冲激光器"),
            ("Aurelian energy turret.", "奥雷利安能量炮台。"),
            ("Heavy Beam Laser", "重型光束激光器"),
            ("Capital-grade beam, cruiser+ punch.", "旗舰级光束，巡洋舰级以上的打击力。"),
            ("Railgun", "磁轨炮"),
            ("Kaldari long-range hybrid turret.", "卡尔达里远程混合炮台。"),
            ("Missile Launcher", "导弹发射器"),
            ("Launches seeker missiles.", "发射追踪导弹。"),
            ("Ion Blaster", "离子疾炮"),
            ("Meridian close-range hybrid turret.", "子午线近程混合炮台。"),
            ("Autocannon", "自动加农炮"),
            ("Varkhald rapid projectile turret.", "瓦克哈德速射弹道炮台。"),
            ("Mining Laser", "采矿激光器"),
            ("Extracts ore from asteroids.", "从小行星中采掘矿石。"),
            ("Shield Booster", "护盾增效器"),
            ("Active shield restoration burst.", "主动式护盾修复脉冲。"),
            ("Afterburner", "加力燃烧器"),
            ("Toggle: +80% sublight speed.", "开关：亚光速速度 +80%。"),
            ("Armor Repairer", "装甲维修器"),
            ("Active armor restoration.", "主动式装甲修复。"),
            ("Shield Extender", "护盾扩展器"),
            ("Passive: +200 max shield.", "被动：护盾上限 +200。"),
            ("Armor Plate", "装甲附甲"),
            ("Passive: +250 max armor.", "被动：装甲上限 +250。"),
            ("Weapon Amplifier", "武器放大器"),
            ("Passive: +18% weapon damage.", "被动：武器伤害 +18%。"),
            ("Cargo Expander", "货舱扩展器"),
            ("Passive: +250 m3 cargo.", "被动：货舱容积 +250 立方米。"),

            // --- Items ---
            ("Ferrite Ore", "铁素体矿石"),
            ("Common high-security ore.", "高安区常见矿石。"),
            ("Novacite Ore", "新星矿石"),
            ("Uncommon low-security ore.", "低安区少见的矿石。"),
            ("Crystalline Ore", "结晶矿石"),
            ("Rare null-security ore.", "零安区稀有矿石。"),
            ("Sealed Cargo", "密封货物"),
            ("Mission cargo. Handle with care.", "任务货物。请小心搬运。"),

            // --- Skills ---
            ("Spaceship Command", "舰船操控"),
            ("Gates access to larger ship classes.", "解锁更大舰船等级的驾驶资格。"),
            ("Gunnery", "炮术"),
            ("+4% turret damage per level.", "每级 +4% 炮台伤害。"),
            ("Missile Operation", "导弹操控"),
            ("+4% missile damage per level.", "每级 +4% 导弹伤害。"),
            ("Mining", "采矿"),
            ("+5% mining yield per level.", "每级 +5% 采矿产出。"),
            ("Shield Operation", "护盾操作"),
            ("+5% shield restoration per level.", "每级 +5% 护盾修复量。"),
            ("Mechanics", "机械学"),
            ("+5% armor repair amount per level.", "每级 +5% 装甲维修量。"),
            ("Navigation", "导航学"),
            ("+5% sublight speed per level.", "每级 +5% 亚光速速度。"),
        };

        // Fixed labels that must win over token-by-token transliteration.
        private static readonly (string en, string zh)[] NameExactEntries =
        {
            ("Directorate Bureau", "理事会分局"),
            // Faction full names (storyline agent names append " Command").
            ("Aurelian Empire", "奥雷利安帝国"),
            ("Kaldari State", "卡尔达里合众国"),
            ("Meridian Federation", "子午线联邦"),
            ("Varkhald Republic", "瓦克哈德共和国"),
            ("Blood Reavers", "血掠者"),
            ("Nathari Hive", "纳萨里蜂巢"),
            ("Crimson Hand", "绯红之手"),
            ("Ashfang Cartel", "灰牙卡特尔"),
            ("Sisters of the Veil", "面纱修会"),
            ("The Directorate", "理事会"),
        };

        // Closed UniverseGenerator vocabulary: system-name syllables, name suffixes,
        // station/belt labels, person names, ship names and the "Elite" prefix.
        // Unknown tokens (faction abbreviations, roman numerals, numbers) pass through.
        private static readonly (string en, string zh)[] NameFragmentEntries =
        {
            // System name syllables, part A (leading)
            ("Al", "阿尔"), ("Bel", "贝尔"), ("Cor", "科尔"), ("Dur", "杜尔"),
            ("El", "埃尔"), ("Fen", "芬"), ("Gal", "加尔"), ("Hel", "赫尔"),
            ("Ith", "伊斯"), ("Jor", "乔尔"), ("Ka", "卡"), ("Lyr", "利尔"),
            ("Mor", "莫尔"), ("Nym", "尼姆"), ("Ost", "奥斯特"), ("Per", "佩尔"),
            ("Qua", "库阿"), ("Ryn", "林"), ("Sel", "塞尔"), ("Tor", "托尔"),
            ("Ul", "乌尔"), ("Vex", "维克斯"), ("Wyn", "温"), ("Xan", "赞"),
            ("Yor", "约尔"), ("Zel", "泽尔"), ("Ash", "阿什"), ("Bra", "布拉"),
            ("Cyn", "辛"), ("Dra", "德拉"),

            // System name syllables, part B (trailing)
            ("ara", "拉"), ("eth", "斯"), ("ion", "翁"), ("os", "斯"),
            ("une", "温"), ("ax", "克斯"), ("ir", "伊尔"), ("on", "恩"),
            ("ea", "娅"), ("ys", "斯"), ("oth", "奥斯"), ("ael", "艾尔"),
            ("mir", "米尔"), ("is", "丝"), ("ur", "乌尔"),

            // System name suffixes (roman numerals pass through and merge without a space)
            ("Prime", "主星"), ("Major", "大星"), ("Minor", "小星"),

            // Belt suffixes
            ("Alpha", "α"), ("Beta", "β"), ("Gamma", "γ"), ("Delta", "δ"),

            // Station labels
            ("Outpost", "前沿哨站"), ("Pirate Haven", "海盗窝点"),
            ("Sanctuary", "避难圣所"), ("Refuge", "庇护所"),
            ("Directorate", "理事会"), ("Bureau", "分局"),

            // Person names, first
            ("Aren", "阿伦"), ("Bela", "贝拉"), ("Corin", "科林"), ("Dara", "达拉"),
            ("Elias", "伊莱亚斯"), ("Freya", "芙蕾雅"), ("Goran", "戈兰"), ("Hana", "哈娜"),
            ("Ivan", "伊万"), ("Jora", "乔拉"), ("Kell", "凯尔"), ("Lena", "莉娜"),
            ("Marek", "马雷克"), ("Nadia", "娜迪亚"), ("Orin", "奥林"), ("Petra", "佩特拉"),
            ("Quill", "奎尔"), ("Rosa", "罗莎"), ("Sten", "斯滕"), ("Talia", "塔莉亚"),
            ("Ulric", "乌尔里克"), ("Vera", "薇拉"), ("Wren", "雷恩"), ("Xavier", "泽维尔"),
            ("Yara", "雅拉"), ("Zane", "赞恩"),

            // Person names, last
            ("Voss", "沃斯"), ("Kaine", "凯恩"), ("Ardath", "阿达斯"), ("Belmore", "贝尔莫"),
            ("Castellan", "卡斯特兰"), ("Draven", "德雷文"), ("Erland", "厄兰"), ("Falk", "法尔克"),
            ("Greer", "格里尔"), ("Haldane", "霍尔丹"), ("Ivar", "伊瓦尔"), ("Jorund", "约伦德"),
            ("Korr", "科尔"), ("Lindqvist", "林德奎斯特"), ("Moreau", "莫罗"), ("Nyx", "尼克斯"),
            ("Okafor", "奥卡福"), ("Pryce", "普莱斯"), ("Quade", "奎德"), ("Reyes", "雷耶斯"),
            ("Sorren", "索伦"), ("Thane", "塞恩"), ("Umar", "乌马尔"), ("Valen", "瓦伦"),
            ("Ward", "沃德"), ("Yilmaz", "伊尔马兹"),

            // NPC ship-name tokens
            ("Elite", "精英"),
        };

        // Fragments that merge onto the preceding transliteration without a space,
        // so "Helara Prime" becomes 赫尔拉主星 rather than 赫尔拉 主星.
        private static readonly string[] MergeSuffixWords =
        {
            "I", "II", "III", "IV", "V", "VI", "VII", "VIII",
            "Prime", "Major", "Minor",
            "Outpost", "Pirate Haven", "Sanctuary", "Refuge", "Directorate", "Bureau",
            "Star",
        };

        // The leading/trailing syllables UniverseGenerator concatenates without a
        // separator to form system names ("Hel" + "ara" = "Helara"). Used only for
        // greedy decomposition of otherwise unknown single tokens.
        private static readonly string[] SyllableVocabulary =
        {
            "Al", "Bel", "Cor", "Dur", "El", "Fen", "Gal", "Hel", "Ith", "Jor", "Ka", "Lyr",
            "Mor", "Nym", "Ost", "Per", "Qua", "Ryn", "Sel", "Tor", "Ul", "Vex", "Wyn", "Xan",
            "Yor", "Zel", "Ash", "Bra", "Cyn", "Dra",
            "ara", "eth", "ion", "os", "une", "ax", "ir", "on", "ea", "ys", "oth", "ael",
            "mir", "is", "ur",
        };

        // LegacyV1Importer validation messages surfaced through the import log.
        private static readonly (string en, string zh)[] LegacyEntries =
        {
            ("Legacy save is empty.", "旧版存档为空。"),
            ("Legacy save exceeds the {0} byte limit.", "旧版存档超过 {0} 字节上限。"),
            ("Legacy save contains trailing JSON content.", "旧版存档包含多余的 JSON 内容。"),
            ("Expected legacy version 1, got {0}.", "期望旧版版本号为 1，实际为 {0}。"),
            ("Duplicate ship instance ID '{0}'.", "舰船实例 ID “{0}”重复。"),
            ("Duplicate mission ID '{0}'.", "任务 ID “{0}”重复。"),
            ("Required field '{0}' is missing.", "必填字段“{0}”缺失。"),
            ("Field '{0}' {1}.", "字段“{0}”{1}。"),
            ("{0} must be finite.", "{0} 必须为有限值。"),
            ("{0} references unknown instance '{1}'.", "“{0}”引用了未知实例“{1}”。"),
            ("{0} references unknown ID '{1}'.", "“{0}”引用了未知 ID“{1}”。"),
            ("must fit an unsigned 32-bit integer", "必须符合无符号 32 位整数范围"),
            ("must be non-negative", "必须为非负数"),
            ("must match player.location.systemId", "必须与 player.location.systemId 一致"),
            ("must contain at least one ship", "必须包含至少一艘舰船"),
            ("must contain only objects", "必须只包含对象"),
            ("entries must be module IDs or null", "条目必须是模块 ID 或 null"),
            ("must be a non-empty reference ID or null", "必须是非空引用 ID 或 null"),
            ("must be a non-empty string", "必须是非空字符串"),
            ("must be an integer", "必须是整数"),
            ("must be numeric", "必须是数字"),
            ("is outside the supported integer range", "超出支持的整数范围"),
        };
    }
}
