using Microsoft.EntityFrameworkCore.Migrations;

namespace MiraiNote.Data.Migrations;

/// <summary>
/// AddWelcomePhrase 的种子。已经应用到库上之后不要改这份清单，否则只有新库会变。
/// </summary>
public static class WelcomePhraseMigrationSeed
{
    private static readonly DateTime At = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    public static int PoemCount => Rows.Count(row => row.Kind == "poem");

    public static void Insert(MigrationBuilder migrationBuilder)
    {
        var id = 1;
        foreach (var row in Rows)
        {
            migrationBuilder.InsertData(
                table: "WelcomePhrase",
                columns:
                [
                    "Id", "Kind", "Text", "Author", "Source", "Period", "Special", "Season",
                    "IsEnabled", "SortOrder", "IsDeleted", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy",
                ],
                values:
                [
                    id,
                    row.Kind,
                    row.Text,
                    row.Author,
                    row.Source,
                    row.Period,
                    row.Special,
                    row.Season,
                    true,
                    id,
                    false,
                    At,
                    1,
                    At,
                    1,
                ]);
            id++;
        }
    }

    private readonly record struct Row(
        string Kind,
        string Text,
        string? Author,
        string? Source,
        string? Period,
        string? Special,
        string? Season);

    private static Row Greet(string period, string text) =>
        new("greeting", text, null, null, period, null, null);

    private static Row Special(string special, string text) =>
        new("greeting", text, null, null, null, special, null);

    private static Row Poem(string season, string text, string author, string source) =>
        new("poem", text, author, source, null, null, season);

    private static readonly Row[] Rows =
    [
        Greet("morning", "早安，{name}"),
        Greet("morning", "早上好，{name}"),
        Greet("morning", "新的一天，{name}"),
        Greet("morning", "{name}，早"),
        Greet("noon", "中午好，{name}"),
        Greet("noon", "{name}，记得吃饭"),
        Greet("noon", "午安，{name}"),
        Greet("noon", "{name}，歇一会儿吧"),
        Greet("afternoon", "下午好，{name}"),
        Greet("afternoon", "{name}，喝杯茶？"),
        Greet("afternoon", "下午也加油，{name}"),
        Greet("afternoon", "{name}，下午好呀"),
        Greet("evening", "晚上好，{name}"),
        Greet("evening", "{name}，今天辛苦了"),
        Greet("evening", "晚上好，{name}，放松一下"),
        Greet("evening", "{name}，晚上好呀"),
        Greet("latenight", "夜深了，{name}"),
        Greet("latenight", "{name}，早点休息"),
        Greet("latenight", "还没睡呀，{name}"),
        Greet("latenight", "夜深了，{name}，别熬太晚"),
        Special("rain", "下雨了，{name}，记得带伞"),
        Special("friday", "周五了，{name}"),

        Poem("spring", "春眠不觉晓，处处闻啼鸟。", "孟浩然", "春晓"),
        Poem("spring", "国破山河在，城春草木深。", "杜甫", "春望"),
        Poem("spring", "好雨知时节，当春乃发生。", "杜甫", "春夜喜雨"),
        Poem("spring", "两个黄鹂鸣翠柳，一行白鹭上青天。", "杜甫", "绝句"),
        Poem("spring", "迟日江山丽，春风花草香。", "杜甫", "绝句二首"),
        Poem("spring", "春风又绿江南岸，明月何时照我还。", "王安石", "泊船瓜洲"),
        Poem("spring", "竹外桃花三两枝，春江水暖鸭先知。", "苏轼", "惠崇春江晚景"),
        Poem("spring", "春色满园关不住，一枝红杏出墙来。", "叶绍翁", "游园不值"),
        Poem("spring", "等闲识得东风面，万紫千红总是春。", "朱熹", "春日"),
        Poem("spring", "沾衣欲湿杏花雨，吹面不寒杨柳风。", "志南", "绝句"),
        Poem("spring", "天街小雨润如酥，草色遥看近却无。", "韩愈", "早春呈水部张十八员外"),
        Poem("spring", "乱花渐欲迷人眼，浅草才能没马蹄。", "白居易", "钱塘湖春行"),
        Poem("spring", "不知细叶谁裁出，二月春风似剪刀。", "贺知章", "咏柳"),
        Poem("spring", "野火烧不尽，春风吹又生。", "白居易", "赋得古原草送别"),
        Poem("spring", "羌笛何须怨杨柳，春风不度玉门关。", "王之涣", "凉州词"),

        Poem("summer", "接天莲叶无穷碧，映日荷花别样红。", "杨万里", "晓出净慈寺送林子方"),
        Poem("summer", "毕竟西湖六月中，风光不与四时同。", "杨万里", "晓出净慈寺送林子方"),
        Poem("summer", "小荷才露尖尖角，早有蜻蜓立上头。", "杨万里", "小池"),
        Poem("summer", "泉眼无声惜细流，树阴照水爱晴柔。", "杨万里", "小池"),
        Poem("summer", "绿树阴浓夏日长，楼台倒影入池塘。", "高骈", "山亭夏日"),
        Poem("summer", "水晶帘动微风起，满架蔷薇一院香。", "高骈", "山亭夏日"),
        Poem("summer", "明月别枝惊鹊，清风半夜鸣蝉。", "辛弃疾", "西江月·夜行黄沙道中"),
        Poem("summer", "稻花香里说丰年，听取蛙声一片。", "辛弃疾", "西江月·夜行黄沙道中"),
        Poem("summer", "七八个星天外，两三点雨山前。", "辛弃疾", "西江月·夜行黄沙道中"),
        Poem("summer", "荷风送香气，竹露滴清响。", "孟浩然", "夏日南亭怀辛大"),
        Poem("summer", "黄梅时节家家雨，青草池塘处处蛙。", "赵师秀", "约客"),
        Poem("summer", "梅子金黄杏子肥，麦花雪白菜花稀。", "范成大", "四时田园杂兴"),
        Poem("summer", "日长篱落无人过，惟有蜻蜓蛱蝶飞。", "范成大", "四时田园杂兴"),
        Poem("summer", "昼出耘田夜绩麻，村庄儿女各当家。", "范成大", "四时田园杂兴"),
        Poem("summer", "力尽不知热，但惜夏日长。", "白居易", "观刈麦"),

        Poem("autumn", "空山新雨后，天气晚来秋。", "王维", "山居秋暝"),
        Poem("autumn", "明月松间照，清泉石上流。", "王维", "山居秋暝"),
        Poem("autumn", "停车坐爱枫林晚，霜叶红于二月花。", "杜牧", "山行"),
        Poem("autumn", "远上寒山石径斜，白云生处有人家。", "杜牧", "山行"),
        Poem("autumn", "月落乌啼霜满天，江枫渔火对愁眠。", "张继", "枫桥夜泊"),
        Poem("autumn", "银烛秋光冷画屏，轻罗小扇扑流萤。", "杜牧", "秋夕"),
        Poem("autumn", "长安一片月，万户捣衣声。", "李白", "子夜吴歌·秋歌"),
        Poem("autumn", "露从今夜白，月是故乡明。", "杜甫", "月夜忆舍弟"),
        Poem("autumn", "无边落木萧萧下，不尽长江滚滚来。", "杜甫", "登高"),
        Poem("autumn", "自古逢秋悲寂寥，我言秋日胜春朝。", "刘禹锡", "秋词"),
        Poem("autumn", "荷尽已无擎雨盖，菊残犹有傲霜枝。", "苏轼", "赠刘景文"),
        Poem("autumn", "一年好景君须记，最是橙黄橘绿时。", "苏轼", "赠刘景文"),
        Poem("autumn", "萧萧梧叶送寒声，江上秋风动客情。", "叶绍翁", "夜书所见"),
        Poem("autumn", "君问归期未有期，巴山夜雨涨秋池。", "李商隐", "夜雨寄北"),
        Poem("autumn", "湖光秋月两相和，潭面无风镜未磨。", "刘禹锡", "望洞庭"),

        Poem("winter", "千山鸟飞绝，万径人踪灭。", "柳宗元", "江雪"),
        Poem("winter", "孤舟蓑笠翁，独钓寒江雪。", "柳宗元", "江雪"),
        Poem("winter", "忽如一夜春风来，千树万树梨花开。", "岑参", "白雪歌送武判官归京"),
        Poem("winter", "瀚海阑干百丈冰，愁云惨淡万里凝。", "岑参", "白雪歌送武判官归京"),
        Poem("winter", "纷纷暮雪下辕门，风掣红旗冻不翻。", "岑参", "白雪歌送武判官归京"),
        Poem("winter", "墙角数枝梅，凌寒独自开。", "王安石", "梅花"),
        Poem("winter", "遥知不是雪，为有暗香来。", "王安石", "梅花"),
        Poem("winter", "梅须逊雪三分白，雪却输梅一段香。", "卢梅坡", "雪梅"),
        Poem("winter", "终南阴岭秀，积雪浮云端。", "祖咏", "终南望余雪"),
        Poem("winter", "夜深知雪重，时闻折竹声。", "白居易", "夜雪"),
        Poem("winter", "柴门闻犬吠，风雪夜归人。", "刘长卿", "逢雪宿芙蓉山主人"),
        Poem("winter", "燕山雪花大如席，片片吹落轩辕台。", "李白", "北风行"),
        Poem("winter", "千里黄云白日曛，北风吹雁雪纷纷。", "高适", "别董大"),
        Poem("winter", "晚来天欲雪，能饮一杯无。", "白居易", "问刘十九"),
        Poem("winter", "云横秦岭家何在，雪拥蓝关马不前。", "韩愈", "左迁至蓝关示侄孙湘"),
    ];
}
