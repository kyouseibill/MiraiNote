# 家务通知

Bark 和邮件提醒默认关闭。设置页和「发送测试」在关闭时仍然可用，调度器不会发出任何到期、逾期或补货通知。

## 配置

不要把真实 Bark 地址、加密密钥或 SMTP 授权码写进仓库。密钥只放在部署环境的环境变量或服务器自己的配置里。

生成 Bark 地址加密密钥（不要把输出提交到 git）：

```bash
openssl rand -base64 32
```

配置形状如下。尖括号里的内容是说明，不是可以提交的取值。

```json
{
  "Household": {
    "PublicBaseUrl": "https://example.invalid",
    "Notifications": {
      "Enabled": false,
      "ProtectionKey": "<openssl rand -base64 32 的输出>",
      "BarkAllowedHosts": []
    }
  },
  "Email": {
    "SmtpHost": "smtp.example.invalid",
    "SmtpPort": 587,
    "SmtpUser": "notifier@example.invalid",
    "SmtpPassword": "<部署环境里的 SMTP 授权码>",
    "FromAddress": "notifier@example.invalid",
    "FromName": "MiraiNote",
    "UseSsl": false
  }
}
```

| 配置项 | 环境变量 | 默认值 |
| --- | --- | --- |
| `Household:Notifications:Enabled` | `Household__Notifications__Enabled` | `false`。设为 `true` 才按分钟发送。 |
| `Household:Notifications:ProtectionKey` | `Household__Notifications__ProtectionKey` | 空。没配这个密钥时不能保存 Bark 地址，但可以用表单里的地址发送测试。用上面的 `openssl` 命令生成。 |
| `Household:Notifications:BarkAllowedHosts` | `Household__Notifications__BarkAllowedHosts__0`、`__1` … | 空数组。`api.day.app` 始终放行，这里只追加自建 Bark 主机，不要写协议。例如 `Household__Notifications__BarkAllowedHosts__0=bark.example.com`。 |
| `Household:PublicBaseUrl` | `Household__PublicBaseUrl` | 空。事项详情的公网根地址。Bark 的点击链接和邮件里的「查看事项」都从这里拼，并且只接受 http/https。 |
| `Email:SmtpHost` 等 | `Email__SmtpHost`、`Email__SmtpPort`、`Email__SmtpUser`、`Email__SmtpPassword`、`Email__FromAddress`、`Email__FromName`、`Email__UseSsl` | 复用现有 SMTP。主机为空或发送失败时只记错误类型，不记授权码，也不挡住 Bark。 |

QQ 邮箱要等 SMTP 授权码重新生成后再填到部署环境，不要提交到 git。

## 成员设置

每个人只能读写自己的设置。`PUT /api/v1/household/notification-settings` 是整体替换：没传的推送时间、通道、开关和逾期间隔会回到默认（09:00、提前走邮件、到期和逾期走 Bark、间隔 3 天、两个通道都开）。Bark 地址留空表示不修改；`clearBarkAddress: true` 才会清掉。

- Bark、邮件可以分别开关。
- Bark 地址只接受 https，默认只允许 `api.day.app`。自建主机要先加到 `BarkAllowedHosts`。地址加密后存在服务器。接口只返回「已配置」和末 4 位，日志里也不写完整地址。
- 发送前会解析 DNS。解析结果里只要有环回、私网、链路本地、CGNAT 或组播地址（含 IPv6 和 IPv4-mapped IPv6），这次请求就不会发出。不会自动跟随重定向。超时大约 5 秒。
- 邮件只会发到本人账号邮箱，不能填其他地址。要换邮箱就改账号邮箱。测试邮件和定时邮件都按用户限流：每分钟 3 次、每小时 20 次。测试 Bark 同样限流。超过后返回「发送过于频繁，请稍后再试」。
- 测试发送失败时，接口只返回「测试通知发送失败」，不区分超时和连接拒绝。
- 每天推送时间默认 09:00，北京时间，精确到分钟。
- 提前 N 天默认走邮件，到期当天和逾期默认走 Bark。逾期重复间隔默认 3 天。
- 事项上的提前提醒天数沿用已有字段，默认 7。

有负责人时只通知负责人，否则通知全体成员。负责人被移出家庭后，该家庭里他负责的事项自动变成未指派，之后按未指派规则提醒所有成员。

事项到期提醒会带上关联耗材的库存，例如「库存 0，需先买」。补货提醒走该成员已开启的通道；每个耗材每次低库存只提醒一次，补货后重置。

调度是后台每分钟一次，时间来自家务时钟（Asia/Shanghai）。Testing 环境打开测试时钟后，提醒和限流窗口也跟着测试时钟走。

服务器离线期间漏掉的提醒，恢复后每个事项每个成员只补 1 条，内容用当前状态。同一事项、成员、日期、通道有唯一约束；唯一冲突视为其他执行已经占住，不重复发送，也不把这次冲突打成错误堆栈。已完成、暂停、归档或删除的事项在发送前会再查一次，这些状态记为 Skipped，不占住当天。事项当天恢复后仍会发送。发送失败记为 Failed，当天最多再试 3 次（先等 1 分钟，再等 5 分钟）。失败摘要只记异常类型。

修改推送时间从下一次生效：当天已经发过的不重发，还没发的按新时间发。

同样内容在 3 秒内换一个新的幂等键再点完成，接口返回 409。前端提示「刚刚已提交过，请稍后再试」。

## 怎样测试 Bark

1. 在 Bark App 里复制自己的推送地址，形如 `https://api.day.app/<device-key>`。不要把这串地址发到仓库、工单或日志。自建 Bark 必须是 https，并把主机名追加到 `BarkAllowedHosts`。
2. 确认部署环境已经配置 `Household__Notifications__ProtectionKey`。没有这个密钥时，测试按钮仍然可以直接用输入框里的地址，但保存会失败。
3. 打开家务周期的「通知」，粘贴地址，点 Bark 的「发送测试」。
4. 手机应收到级别为 time-sensitive 的测试通知。保存后页面只显示末 4 位。
5. 要验证点击跳转，把 `Household__PublicBaseUrl` 设成测试服地址（例如 Cloudflare 隧道）。隧道需要登录，测完关闭。然后把 `Household__Notifications__Enabled` 设为 `true`，到推送时间应收到事项提醒，点开进入 `/household/items/{id}`。
6. 测完把总开关改回 `false`，并确认仓库和日志里没有那条 Bark 地址。
