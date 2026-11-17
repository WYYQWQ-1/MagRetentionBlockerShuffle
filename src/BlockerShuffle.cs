using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

namespace MagRetentionBlockerShuffle
{
    /// <summary>
    /// 挡路件重排：主 mod 找不到落点（腾出的格、弹挂/口袋空位都不行）时，
    /// 挪开一件挡路的东西，把旧弹匣留在弹挂里。
    ///
    /// 分工：我们只决定“放哪”，把 Move/Add 塞进 operations；事件与换弹失败时的回滚由主 mod 负责。
    /// 规则与主 mod 一致：不碰背包（换弹不能白送背包空间）。
    /// 两端一致：候选按（格子面积, 格子ID, 物品 y, x, Id）排序。
    /// </summary>
    internal static class BlockerShuffle
    {
        internal static bool TryShuffle(Magazine oldMagazine, ItemAddress freedSlot,
                                        ItemController itemController, List<IOperationResult> operations)
        {
            if (oldMagazine == null || itemController == null || operations == null) return false;
            if (!(freedSlot is GridItemAddress freed)) return false;

            var equipment = (itemController as InventoryController)?.Inventory?.Equipment;
            if (equipment == null) return false;

            var grids = equipment.GetPrioritizedGridsForUnloadedObject(backpackIncluded: false)
                .Where(g => g != null)
                .OrderBy(g => g.GridWidth * g.GridHeight)
                .ThenBy(g => g.ID, StringComparer.Ordinal)
                .ToList();
            if (grids.Count == 0) return false;

            // ① 挡路件塞进“新弹匣腾出的那格”，旧弹匣占它的位置。
            //    腾出的格必须在弹挂/口袋里，否则等于把东西挪进背包。
            bool freedSlotIsCarried = grids.Any(g => ReferenceEquals(g, freed.Grid));
            if (freedSlotIsCarried)
            {
                foreach (var grid in grids)
                {
                    if (ReferenceEquals(grid, freed.Grid)) continue;
                    foreach (var blocker in Candidates(grid))
                    {
                        var move = ItemManipulator.Move(blocker, freedSlot, itemController, simulate: false);
                        if (move.Failed) continue;

                        try
                        {
                            var spot = FindSpot(grid, exclude: blocker, target: oldMagazine);
                            if (spot != null)
                            {
                                var add = ItemManipulator.Add(oldMagazine, spot, itemController, simulate: false);
                                if (add.Succeeded)
                                {
                                    operations.Add(move.Value);
                                    operations.Add(add.Value);
                                    Plugin.Log?.LogInfo($"[KMR][addon] shuffle A: {blocker.ShortName} -> {freed.ContainerName}");
                                    return true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log?.LogWarning("[KMR][addon] shuffle A: " + ex.Message);
                        }
                        move.Value.RollBack();
                    }
                }
            }

            // ② 同格：先把挡路件挪到别处，再让旧弹匣占它的位置。
            //    顺序必须是 destAddr → Move → 重算 spot，否则两张图同源会把落点算到同一个矩形上。
            foreach (var grid in grids)
            {
                foreach (var blocker in Candidates(grid))
                {
                    foreach (var dest in grids)
                    {
                        var destAddr = FindSpot(dest, exclude: null, target: blocker);
                        if (destAddr == null) continue;

                        var move = ItemManipulator.Move(blocker, destAddr, itemController, simulate: false);
                        if (move.Failed) continue;

                        try
                        {
                            var spot = FindSpot(grid, exclude: null, target: oldMagazine);
                            if (spot != null)
                            {
                                var add = ItemManipulator.Add(oldMagazine, spot, itemController, simulate: false);
                                if (add.Succeeded)
                                {
                                    operations.Add(move.Value);
                                    operations.Add(add.Value);
                                    Plugin.Log?.LogInfo($"[KMR][addon] shuffle B: {blocker.ShortName} -> {dest.ID}");
                                    return true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log?.LogWarning("[KMR][addon] shuffle B: " + ex.Message);
                        }
                        move.Value.RollBack();
                    }
                }
            }

            return false;
        }

        /// <summary>候选顺序：位置 (y, x) → Id，保证两端挑同一件。</summary>
        private static List<Item> Candidates(Grid grid)
        {
            var list = new List<Item>();
            foreach (var item in grid.Items)
            {
                var addr = item.CurrentAddress as GridItemAddress;
                if (addr == null) continue;
                if (!CanRelocate(item)) continue;
                list.Add(item);
            }
            list.Sort((a, b) =>
            {
                var aa = (GridItemAddress)a.CurrentAddress;
                var bb = (GridItemAddress)b.CurrentAddress;
                int c = aa.LocationInGrid.y.CompareTo(bb.LocationInGrid.y);
                if (c != 0) return c;
                c = aa.LocationInGrid.x.CompareTo(bb.LocationInGrid.x);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        private static bool CanRelocate(Item item)
        {
            try
            {
                if (item == null) return false;
                if (item.PinLockState != EItemPinLockState.Free) return false;
                if (item.QuestItem) return false;
                if (!item.CanBePickedUp) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>纯占位图算落点（不移动真实物品，避免界面残留幽灵格）。</summary>
        private static ItemAddress FindSpot(Grid grid, Item exclude, Item target)
        {
            try
            {
                if (grid == null || target == null) return null;
                int w = grid.GridWidth, h = grid.GridHeight;
                if (w <= 0 || h <= 0) return null;

                var occ = new bool[w, h];
                foreach (var item in grid.Items)
                {
                    if (ReferenceEquals(item, exclude)) continue;
                    var addr = item.CurrentAddress as GridItemAddress;
                    if (addr == null) continue;
                    Paint(occ, grid, item, addr.LocationInGrid);
                }

                var size = CellSize(target);
                for (int rot = 0; rot < 2; rot++)
                {
                    int rw = rot == 0 ? size.X : size.Y;
                    int rh = rot == 0 ? size.Y : size.X;
                    if (rw > w || rh > h) continue;
                    for (int x = 0; x + rw <= w; x++)
                        for (int y = 0; y + rh <= h; y++)
                            if (Free(occ, x, y, rw, rh))
                                return grid.CreateItemAddress(new LocationInGrid(x, y,
                                    rot == 0 ? ItemRotation.Horizontal : ItemRotation.Vertical));
                }
            }
            catch { }
            return null;
        }

        private static void Paint(bool[,] occ, Grid grid, Item item, LocationInGrid at)
        {
            var size = CellSize(item);
            int w = at.r == ItemRotation.Vertical ? size.Y : size.X;
            int h = at.r == ItemRotation.Vertical ? size.X : size.Y;
            for (int x = at.x; x < at.x + w && x < grid.GridWidth; x++)
                for (int y = at.y; y < at.y + h && y < grid.GridHeight; y++)
                    if (x >= 0 && y >= 0) occ[x, y] = true;
        }

        private static bool Free(bool[,] occ, int ox, int oy, int w, int h)
        {
            for (int x = ox; x < ox + w; x++)
                for (int y = oy; y < oy + h; y++)
                    if (occ[x, y]) return false;
            return true;
        }

        private static IntVec2 CellSize(Item item)
        {
            try { return item.CalculateCellSize(); } catch { return new IntVec2(1, 1); }
        }
    }
}
