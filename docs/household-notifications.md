# 家务通知

Bark 和邮件提醒默认关闭。设置页和「发送测试」在关闭时仍然可用，调度器不会发出任何到期、逾期或补货通知。

## 配置

不要把真实 Bark 地址、加密密钥或 SMTP 授权码写进仓库。下面都是占位符，用环境变量或部署环境自己的配置覆盖。

```json
{
  "Household": {
    "PublicBaseUrl": "https://example.invalid",
    "Notifications": {
      "Enabled": false,
      "ProtectionKey": "change-me"
    }
  },
  "Email": {
    "SmtpHost": "smtp.example.invalid",
    "SmtpPort": 587,
    "SmtpUser": "notifier@example.invalid",
    "SmtpPassword": "change-me",
    "FromAddress": "notifier@example.invalid",
    "FromName": "MiraiNote",
    "UseSsl": false
  }
}
```

对应环境变量：

| 变量 | 作用 |
| --- | --- |
| `Household__Notifications__Enabled` | `true` 才按分钟发送。默认 `false`。 |
| `Household__Notifications__ProtectionKey` | Bark 地址的加密密钥。不写进代码。没配这个密钥时不能保存 Bark 地址，但可以用表单里的地址发送测试。 |
| `Household__PublicBaseUrl` | 事项详情的公网根地址，例如 `https://example.invalid`。Bark 的点击链接和邮件里的「查看事项」都从这里拼，并且只接受 http/https。 |
| `Email__SmtpHost` 等 | 复用现有 SMTP。主机为空或发送失败时只记错误类型，不记授权码，也不挡住 Bark。 |

QQ 邮箱要等 SMTP 授权码重新生成后再填到部署环境，不要提交到 git。

## 成员设置

每个人只能读写自己的设置：

- Bark、邮件可以分别开关。
- Bark 地址加密后存在服务器。接口只返回「已配置」和末 4 位，日志里也不写完整地址。
- 收件邮箱只返回给本人。
- 每天推送时间默认 09:00，北京时间，精确到分钟。
- 提前 N 天默认走邮件，到期当天和逾期默认走 Bark。逾期重复间隔默认 3 天。
- 事项上的提前提醒天数沿用已有字段，默认 7。

有负责人时只通知负责人，否则通知全体成员。事项到期提醒会带上关联耗材的库存，例如「库存 0，需先买」。补货提醒走该成员已开启的通道；每个耗材每次低库存只提醒一次，补货后重置。

调度是后台每分钟一次，时间来自家务时钟（Asia/Shanghai）。Testing 环境打开测试时钟后，提醒也跟着测试时钟走。

服务器离线期间漏掉的提醒，恢复后每个事项每个成员只补 1 条，内容用当前状态。同一事项、成员、日期、通道有唯一约束，重复写入视为已发。已完成、暂停、归档或删除的事项在发送前会再查一次，这些状态不发。

修改推送时间从下一次生效：当天已经发过的不重发，还没发的按新时间发。

## 怎样测试 Bark

1. 在 Bark App 里复制自己的推送地址，形如 `https://api.day.app/<device-key>`。不要把这串地址发到仓库、工单或日志。
2. 确认部署环境已经配置 `Household__Notifications__ProtectionKey`。没有这个密钥时，测试按钮仍然可以直接用输入框里的地址，但保存会失败。
3. 打开家务周期的「通知」，粘贴地址，点 Bark 的「发送测试」。
4. 手机应收到级别为 time-sensitive 的测试通知。保存后页面只显示末 4 位。
5. 要验证点击跳转，把 `Household__PublicBaseUrl` 设成测试服地址（例如 Cloudflare 隧道）。隧道需要登录，测完关闭。然后把 `Household__Notifications__Enabled` 设为 `true`，到推送时间应收到事项提醒，点开进入 `/household/items/{id}`。
6. 测完把总开关改回 `false`，并确认仓库和日志里没有那条 Bark 地址。
