using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using EFT.InventoryLogic;
using HarmonyLib;

namespace MagRetentionBlockerShuffle
{
    [BepInPlugin(Guid, "Mag Retention Reload — Blocker Shuffle", "0.1.0")]
    [BepInDependency("de.tosox.magretentionreload", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "yu.smartmagreload.blockershuffle";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        internal void Awake()
        {
            Log = Logger;
            try
            {
                _harmony = new Harmony(Guid);

                var type = AccessTools.TypeByName("Tosox.MagRetentionReload.Helpers.RetentionPlacement");
                if (type == null)
                {
                    Log?.LogError("[KMR][addon] Mag Retention Reload not found. Addon inactive.");
                    return;
                }

                // 显式参数类型：分支里若新旧 Stow 并存，避免选中错误的重载
                // 分支版签名：Stow(Magazine old, Magazine new, ItemAddress freed, ItemController ic, List<IOperationResult> ops) : bool
                var stow = AccessTools.Method(type, "Stow", new[]
                {
                    typeof(Magazine), typeof(Magazine), typeof(ItemAddress), typeof(ItemController), typeof(List<IOperationResult>)
                });
                if (stow == null || stow.ReturnType != typeof(bool))
                {
                    Log?.LogWarning("[KMR][addon] this build of Mag Retention Reload has no operations hook " +
                                    "(needs the addon-operations branch). Addon inactive.");
                    return;
                }

                _harmony.Patch(stow, postfix: new HarmonyMethod(AccessTools.Method(typeof(StowPatch), nameof(StowPatch.Postfix))));
                Log?.LogInfo("[KMR][addon] blocker shuffle attached to MagRetentionReload.RetentionPlacement.Stow");
            }
            catch (Exception ex)
            {
                Log?.LogError("[KMR][addon] install failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }
    }

    internal static class StowPatch
    {
        /// <summary>
        /// 后缀：主 mod 的 Stow 已经试过“腾出的格 + 弹挂/口袋的空位”并失败后，才轮到我们挪挡路件。
        /// 用位置参数绑定，避免上游改参数名导致补丁静默失效。
        /// </summary>
        internal static void Postfix(Magazine __0, ItemAddress __2, ItemController __3,
                                     List<IOperationResult> __4, ref bool __result)
        {
            try
            {
                if (__result) return;                                   // 他已经放好了，别动
                if (BlockerShuffle.TryShuffle(__0, __2, __3, __4)) __result = true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[KMR][addon] postfix: " + ex.Message);
            }
        }
    }
}
