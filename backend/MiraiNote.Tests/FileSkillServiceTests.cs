using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Tools;
using MiraiNote.Shared.Common;
using Xunit;

namespace MiraiNote.Tests;

public class FileSkillServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mirainote-skills-" + Guid.NewGuid().ToString("N"));
    private const int UserId = 17;

    private FileSkillService CreateService() => new(Options.Create(new FileSystemOptions { WorkspaceRoot = _root }));

    [Fact]
    public void Existing_workspace_skill_is_discovered_and_explicitly_loaded()
    {
        var folder = Path.Combine(_root, "users", UserId.ToString(), "skills", "yahoo-transit-jp");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), "---\nname: yahoo-transit-jp\ndescription: 查询日本铁路和公交换乘。\n---\n\n先搜索实时路线，再报告换乘。", System.Text.Encoding.UTF8);

        var service = CreateService();
        var skill = Assert.Single(service.List(UserId));
        Assert.Equal("yahoo-transit-jp", skill.Name);
        Assert.Equal("查询日本铁路和公交换乘。", skill.Description);
        Assert.True(skill.Enabled);
        Assert.Contains("先搜索实时路线", service.BuildPrompt(UserId, "$yahoo-transit-jp 东京到大阪"));
        Assert.Contains("skills/yahoo-transit-jp/SKILL.md", service.BuildPrompt(UserId, "东京到大阪怎么走"));
        Assert.DoesNotContain("先搜索实时路线", service.BuildPrompt(UserId, "东京到大阪怎么走"));
    }

    [Fact]
    public void Disabling_a_skill_removes_it_from_discovery_and_loading_without_erasing_the_file()
    {
        var service = CreateService();
        service.Create(UserId, "route", "---\nname: route\ndescription: 查询路线\n---\n步骤", true);
        service.SetEnabled(UserId, "route", false);

        Assert.False(service.Get(UserId, "route").Enabled);
        Assert.DoesNotContain("---\nname: route", service.BuildPrompt(UserId, "$route 上海"));
        Assert.Contains("不可用", service.BuildPrompt(UserId, "$route 上海"));
        Assert.Throws<BusinessException>(() => service.Load(UserId, "route"));
        Assert.True(File.Exists(Path.Combine(_root, "users", UserId.ToString(), "skills", "route", "SKILL.md")));
    }

    [Fact]
    public void Skills_are_user_scoped_and_slugs_cannot_escape_the_workspace()
    {
        var service = CreateService();
        service.Create(UserId, "route", "---\nname: route\ndescription: 查询路线\n---\n步骤", true);

        Assert.Empty(service.List(UserId + 1));
        Assert.Throws<BusinessException>(() => service.Get(UserId, "../route"));
        Assert.Throws<BusinessException>(() => service.Create(UserId, "../outside", "---\nname: outside\ndescription: x\n---\nx", true));
    }

    [Fact]
    public void Invalid_frontmatter_is_rejected_without_creating_a_skill()
    {
        var service = CreateService();
        Assert.Throws<BusinessException>(() => service.Create(UserId, "route", "# no metadata", true));
        Assert.Empty(service.List(UserId));
    }

    [Fact]
    public void Folded_description_is_used_for_implicit_discovery()
    {
        var service = CreateService();
        service.Create(UserId, "route", "---\nname: route\ndescription: >\n  查询日本铁路与地铁。\n  需要核实换乘。\n---\n步骤", true);
        Assert.Equal("查询日本铁路与地铁。 需要核实换乘。", service.Get(UserId, "route").Description);
    }

    [Fact]
    public void Description_over_500_characters_can_be_saved_and_discovered()
    {
        var service = CreateService();
        var firstLine = new string('甲', 300);
        var secondLine = new string('乙', 300);
        service.Create(UserId, "route", $"---\nname: route\ndescription: >\n  {firstLine}\n  {secondLine}\n---\n步骤", true);

        Assert.Equal($"{firstLine} {secondLine}", service.Get(UserId, "route").Description);
        Assert.Contains(secondLine, service.BuildPrompt(UserId, "需要查询路线"));
    }

    [Fact]
    public void Manifest_without_instructions_is_rejected()
    {
        var service = CreateService();
        Assert.Throws<BusinessException>(() => service.Create(UserId, "route", "---\nname: route\ndescription: 查询路线\n---\n", true));
    }

    [Fact]
    public void Unknown_explicit_skill_receives_a_clear_unavailable_notice()
    {
        var prompt = CreateService().BuildPrompt(UserId, "$missing 查路线");
        Assert.Contains("$missing", prompt);
        Assert.Contains("不可用", prompt);
    }

    [Fact]
    public void Explicit_skill_name_can_end_with_a_hyphen()
    {
        var service = CreateService();
        service.Create(UserId, "route-", "---\nname: route-\ndescription: 查询路线\n---\n核实路线", false);
        Assert.Contains("【显式调用 $route-", service.BuildPrompt(UserId, "$route- 查询路线"));
    }

    [Fact]
    public async Task Load_tool_respects_implicit_invocation_policy()
    {
        var service = CreateService();
        service.Create(UserId, "route", "---\nname: route\ndescription: 查询路线\n---\n步骤", false);
        var tool = new ServerLoadSkillTool(service);

        Assert.Contains("不可用", await tool.ExecuteAsync(UserId, "{\"name\":\"route\"}"));
        Assert.Contains("步骤", service.BuildPrompt(UserId, "$route 上海"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
