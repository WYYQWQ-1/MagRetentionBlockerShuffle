> **Status: waiting on upstream.** This needs a build of Mag Retention Reload that exposes the
> `operations` hook, which is currently on Tosox's `addon-operations` branch and not released yet.
> Until that ships, this addon loads but stays inactive.
> 状态：等上游发布。它需要带 `operations` 钩子的 Mag Retention Reload（目前在 Tosox 的分支上未发布），
> 在那之前本插件会加载但不生效。

# Mag Retention Reload — Blocker Shuffle

A small addon for [Mag Retention Reload](https://github.com/Tosox/SPT-MagRetentionReload) by **Tosox**.
When the main mod cannot find room for the old magazine at all, this moves one blocker aside so the
magazine can stay in the rig instead of dropping to the ground.

**Requires Mag Retention Reload with the addon hook** (the `addon-operations` build). Does nothing on
older builds — it detects the missing hook and stays inactive.

## What it does / 它做什么

- Wraps `RetentionPlacement.Stow` as a **postfix**: the main mod's own placement gets the first try.
  Only when it fails (freed slot + free cells in rig/pockets all tried) does this run.
  仅当主 mod 的 Stow 找不到位置时介入，不抢它的判断。
- Two ways to make room / 两种腾位方式:
  - A: move a blocker into the cell the new magazine just freed / 挡路件塞进新弹匣腾出的那格
  - B: move a blocker elsewhere in the rig/pockets, old magazine takes its place / 挡路件挪走，旧弹匣占位
- Never uses the backpack / secure container — same rule as the main mod.
  不碰背包与安全箱，规则与主 mod 一致。
- Both Move and Add are handed to the main mod as operations, so it raises their events and rolls
  them back if the reload fails. This addon never raises events or touches Fika itself.
  只把操作交回主 mod，事件与回滚都由它负责；本插件不碰网络。

## Install / 安装

```
BepInEx/plugins/MagRetentionBlockerShuffle/MagRetentionBlockerShuffle.dll
```

Plus the main mod (its own folder):

```
BepInEx/plugins/Tosox.MagRetentionReload/Tosox.MagRetentionReload.dll
BepInEx/plugins/Tosox.MagRetentionReload/Tosox.MagRetentionReload.Net.dll
```

- **Co-op: every client needs this addon**, otherwise players place the magazine differently.
  联机时**每个客户端都要装**，否则两边放的位置会不一样。
- No F12 settings. It either attaches or it doesn't; check the log.
  没有 F12 设置项，靠日志确认状态。

## Log lines / 日志

```
[KMR][addon] blocker shuffle attached to MagRetentionReload.RetentionPlacement.Stow
    → attached and active / 已挂上
[KMR][addon] this build of Mag Retention Reload has no operations hook ...
    → main mod too old / 主 mod 版本太旧，本插件不工作
[KMR][addon] shuffle A: <item> -> <cell>
[KMR][addon] shuffle B: <item> -> <grid>
    → it just moved a blocker / 刚刚挪动了一件挡路物
```

## Not verified / 未验证

- Rollback path (a reload cannot be interrupted in normal play).
- Real two-player co-op.

## Credits

The addon hook, `Stow`/`operations` design, and the reference prototype are Tosox's work
([Mag Retention Reload](https://github.com/Tosox/SPT-MagRetentionReload)). This addon is an
independent implementation of the blocker-shuffle placement on top of that hook.

MIT — see `LICENSE`.
