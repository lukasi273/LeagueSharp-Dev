﻿#region License

/*
 Copyright 2014 - 2015 Nikita Bernthaler
 Viktor.cs is part of SFXChallenger.

 SFXChallenger is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, either version 3 of the License, or
 (at your option) any later version.

 SFXChallenger is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with SFXChallenger. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion License

#region

using System;
using System.Collections.Generic;
using System.Linq;
using LeagueSharp;
using LeagueSharp.Common;
using SFXChallenger.Abstracts;
using SFXChallenger.Enumerations;
using SFXChallenger.Helpers;
using SFXChallenger.Managers;
using SFXLibrary;
using SFXLibrary.Extensions.NET;
using SFXLibrary.Logger;
using SharpDX;
using DamageType = SFXChallenger.Enumerations.DamageType;
using MinionManager = SFXLibrary.MinionManager;
using MinionOrderTypes = SFXLibrary.MinionOrderTypes;
using MinionTeam = SFXLibrary.MinionTeam;
using MinionTypes = SFXLibrary.MinionTypes;
using Orbwalking = SFXChallenger.Wrappers.Orbwalking;
using Spell = SFXChallenger.Wrappers.Spell;
using TargetSelector = SFXChallenger.Wrappers.TargetSelector;
using Utils = SFXChallenger.Helpers.Utils;

#endregion

namespace SFXChallenger.Champions
{
    internal class Viktor : Champion
    {
        private const float MaxERange = 1225f;
        private const float ELength = 700f;
        private const float RMoveInterval = 500f;
        private Obj_AI_Base _lastAfterFarmTarget;
        private float _lastAutoAttack;
        private Obj_AI_Base _lastBeforeFarmTarget;
        private Obj_AI_Base _lastQKillableTarget;
        private float _lastRMoveCommand = Environment.TickCount;
        private GameObject _rObject;

        protected override ItemFlags ItemFlags
        {
            get { return ItemFlags.Offensive | ItemFlags.Defensive | ItemFlags.Flee; }
        }

        protected override ItemUsageType ItemUsage
        {
            get { return ItemUsageType.Custom; }
        }

        protected override void OnLoad()
        {
            Core.OnPostUpdate += OnCorePostUpdate;
            Orbwalking.BeforeAttack += OnOrbwalkingBeforeAttack;
            Orbwalking.AfterAttack += OnOrbwalkingAfterAttack;
            AntiGapcloser.OnEnemyGapcloser += OnEnemyGapcloser;
            Interrupter2.OnInterruptableTarget += OnInterruptableTarget;
            CustomEvents.Unit.OnDash += OnUnitDash;
            GameObject.OnCreate += OnGameObjectCreate;
        }

        protected override void OnUnload()
        {
            Core.OnPostUpdate -= OnCorePostUpdate;
            Orbwalking.BeforeAttack -= OnOrbwalkingBeforeAttack;
            Orbwalking.AfterAttack -= OnOrbwalkingAfterAttack;
            AntiGapcloser.OnEnemyGapcloser -= OnEnemyGapcloser;
            Interrupter2.OnInterruptableTarget -= OnInterruptableTarget;
            CustomEvents.Unit.OnDash -= OnUnitDash;
            GameObject.OnCreate -= OnGameObjectCreate;
        }

        protected override void AddToMenu()
        {
            DrawingManager.Add("E " + Global.Lang.Get("G_Max"), MaxERange);

            var comboMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Combo"), Menu.Name + ".combo"));
            HitchanceManager.AddToMenu(
                comboMenu.AddSubMenu(new Menu(Global.Lang.Get("F_MH"), comboMenu.Name + ".hitchance")), "combo",
                new Dictionary<string, HitChance> { { "W", HitChance.VeryHigh }, { "E", HitChance.High } });
            comboMenu.AddItem(new MenuItem(comboMenu.Name + ".q", Global.Lang.Get("G_UseQ")).SetValue(true));
            comboMenu.AddItem(new MenuItem(comboMenu.Name + ".w", Global.Lang.Get("G_UseW")).SetValue(true));
            comboMenu.AddItem(new MenuItem(comboMenu.Name + ".e", Global.Lang.Get("G_UseE")).SetValue(true));

            var harassMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Harass"), Menu.Name + ".harass"));
            HitchanceManager.AddToMenu(
                harassMenu.AddSubMenu(new Menu(Global.Lang.Get("F_MH"), harassMenu.Name + ".hitchance")), "harass",
                new Dictionary<string, HitChance> { { "E", HitChance.VeryHigh } });
            ManaManager.AddToMenu(harassMenu, "harass", ManaCheckType.Minimum, ManaValueType.Percent);
            harassMenu.AddItem(
                new MenuItem(harassMenu.Name + ".auto-attack", Global.Lang.Get("G_UseAutoAttacks")).SetValue(true));
            harassMenu.AddItem(new MenuItem(harassMenu.Name + ".q", Global.Lang.Get("G_UseQ")).SetValue(true));
            harassMenu.AddItem(new MenuItem(harassMenu.Name + ".e", Global.Lang.Get("G_UseE")).SetValue(true));

            var laneclearMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_LaneClear"), Menu.Name + ".lane-clear"));
            ManaManager.AddToMenu(
                laneclearMenu, "lane-clear-q", ManaCheckType.Minimum, ManaValueType.Total, string.Empty, 200, 0, 750);
            laneclearMenu.AddItem(new MenuItem(laneclearMenu.Name + ".q", Global.Lang.Get("G_UseQ")).SetValue(true));
            ManaManager.AddToMenu(laneclearMenu, "lane-clear-e", ManaCheckType.Minimum, ManaValueType.Percent);
            laneclearMenu.AddItem(new MenuItem(laneclearMenu.Name + ".e", Global.Lang.Get("G_UseE")).SetValue(true));
            laneclearMenu.AddItem(
                new MenuItem(laneclearMenu.Name + ".e-min", "E " + Global.Lang.Get("G_Min")).SetValue(
                    new Slider(3, 1, 5)));

            var lasthitMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_LastHit"), Menu.Name + ".lasthit"));
            ManaManager.AddToMenu(lasthitMenu, "lasthit", ManaCheckType.Minimum, ManaValueType.Percent);
            lasthitMenu.AddItem(
                new MenuItem(lasthitMenu.Name + ".q-unkillable", "Q " + Global.Lang.Get("G_Unkillable")).SetValue(true));

            var ultimateMenu = UltimateManager.AddToMenu(Menu, true, true, true, false, false, false, true, true, true);

            ultimateMenu.AddItem(
                new MenuItem(ultimateMenu.Name + ".follow", Global.Lang.Get("Viktor_AutoFollow")).SetValue(true));

            var killstealMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Killsteal"), Menu.Name + ".killsteal"));
            killstealMenu.AddItem(new MenuItem(killstealMenu.Name + ".e", Global.Lang.Get("G_UseE")).SetValue(true));
            killstealMenu.AddItem(
                new MenuItem(killstealMenu.Name + ".q-aa", "Q + " + Global.Lang.Get("G_AutoAttack")).SetValue(true));

            var fleeMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Flee"), Menu.Name + ".flee"));
            fleeMenu.AddItem(new MenuItem(fleeMenu.Name + ".w", Global.Lang.Get("G_UseW")).SetValue(true));
            fleeMenu.AddItem(
                new MenuItem(fleeMenu.Name + ".q-upgraded", "Q " + Global.Lang.Get("G_Upgraded")).SetValue(true));

            var miscMenu = Menu.AddSubMenu(new Menu(Global.Lang.Get("G_Miscellaneous"), Menu.Name + ".miscellaneous"));

            HeroListManager.AddToMenu(
                miscMenu.AddSubMenu(new Menu("W " + Global.Lang.Get("G_Slowed"), miscMenu.Name + "w-slowed")),
                "w-slowed", false, false, true, false);
            HeroListManager.AddToMenu(
                miscMenu.AddSubMenu(new Menu("W " + Global.Lang.Get("G_Immobile"), miscMenu.Name + "w-immobile")),
                "w-immobile", false, false, true, false);
            HeroListManager.AddToMenu(
                miscMenu.AddSubMenu(new Menu("W " + Global.Lang.Get("G_Gapcloser"), miscMenu.Name + "w-gapcloser")),
                "w-gapcloser", false, false, true, false);


            IndicatorManager.AddToMenu(DrawingManager.Menu, true);
            IndicatorManager.Add(
                "Q", delegate(Obj_AI_Hero hero)
                {
                    var damage = 0f;
                    if (Q.IsReady())
                    {
                        damage += Q.GetDamage(hero);
                        damage += CalcPassiveDamage(hero);
                    }
                    else if (HasQBuff())
                    {
                        damage += CalcPassiveDamage(hero);
                    }
                    return damage;
                });
            IndicatorManager.Add(E);
            IndicatorManager.Add(
                "R " + Global.Lang.Get("G_Burst"), delegate(Obj_AI_Hero hero)
                {
                    if (R.IsReady())
                    {
                        return R.GetDamage(hero) + R.GetDamage(hero, 1);
                    }
                    return 0;
                });
            IndicatorManager.Add(
                "R " + Global.Lang.Get("G_Full"), delegate(Obj_AI_Hero hero)
                {
                    if (R.IsReady())
                    {
                        return R.GetDamage(hero) + R.GetDamage(hero, 1) * 10;
                    }
                    return 0;
                });
            IndicatorManager.Finale();
        }

        protected override void SetupSpells()
        {
            Q = new Spell(SpellSlot.Q, Player.BoundingRadius + 600f, DamageType.Magical);
            Q.Range += GameObjects.EnemyHeroes.Select(e => e.BoundingRadius).DefaultIfEmpty(50).Average();
            Q.SetTargetted(0.5f, 1800f);

            W = new Spell(SpellSlot.W, 700f, DamageType.Magical);
            W.SetSkillshot(1.6f, 300f, float.MaxValue, false, SkillshotType.SkillshotCircle);

            E = new Spell(SpellSlot.E, 525f, DamageType.Magical);
            E.SetSkillshot(0f, 90f, 800f, false, SkillshotType.SkillshotLine);

            R = new Spell(SpellSlot.R, 700f, DamageType.Magical);
            R.SetSkillshot(0.2f, 300f, float.MaxValue, false, SkillshotType.SkillshotCircle);
        }

        private void OnCorePostUpdate(EventArgs args)
        {
            try
            {
                if (Menu.Item(Menu.Name + ".ultimate.follow").GetValue<bool>())
                {
                    RFollowLogic();
                }
                if (UltimateManager.Assisted() && R.IsReady())
                {
                    if (Menu.Item(Menu.Name + ".ultimate.assisted.move-cursor").GetValue<bool>())
                    {
                        Orbwalking.MoveTo(Game.CursorPos, Orbwalker.HoldAreaRadius);
                    }
                    var target = TargetSelector.GetTarget(R);
                    if (target != null &&
                        !RLogic(
                            target, Menu.Item(Menu.Name + ".ultimate.assisted.min").GetValue<Slider>().Value,
                            Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady(),
                            Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady()))
                    {
                        if (Menu.Item(Menu.Name + ".ultimate.assisted.duel").GetValue<bool>())
                        {
                            RLogicDuel(
                                Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady(),
                                Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady());
                        }
                    }
                }

                if (UltimateManager.Auto() && R.IsReady())
                {
                    var target = TargetSelector.GetTarget(R);
                    if (target != null &&
                        !RLogic(
                            target, Menu.Item(Menu.Name + ".ultimate.auto.min").GetValue<Slider>().Value,
                            Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady(),
                            Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady(), false, "auto"))
                    {
                        if (Menu.Item(Menu.Name + ".ultimate.auto.duel").GetValue<bool>())
                        {
                            RLogicDuel(
                                Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady(),
                                Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady());
                        }
                    }
                }

                if (HeroListManager.Enabled("w-immobile") && W.IsReady())
                {
                    var target =
                        GameObjects.EnemyHeroes.FirstOrDefault(
                            t =>
                                t.IsValidTarget(W.Range) && HeroListManager.Check("w-immobile", t) &&
                                Utils.IsImmobile(t));
                    if (target != null)
                    {
                        Casting.SkillShot(target, W, HitChance.VeryHigh);
                    }
                }

                if (HeroListManager.Enabled("w-slowed") && W.IsReady())
                {
                    var target =
                        GameObjects.EnemyHeroes.FirstOrDefault(
                            t =>
                                t.IsValidTarget(W.Range) && HeroListManager.Check("w-slowed", t) &&
                                t.Buffs.Any(b => b.Type == BuffType.Slow && b.EndTime - Game.Time > 0.5f));
                    if (target != null)
                    {
                        Casting.SkillShot(target, W, W.GetHitChance("combo"));
                    }
                }

                if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LastHit ||
                    Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LaneClear)
                {
                    if (Menu.Item(Menu.Name + ".lasthit.q-unkillable").GetValue<bool>() && Q.IsReady() &&
                        ManaManager.Check("lasthit"))
                    {
                        var canAttack = Game.Time >= _lastAutoAttack + Player.AttackDelay;
                        var minions =
                            MinionManager.GetMinions(Q.Range)
                                .Where(
                                    m =>
                                        (!canAttack || !Orbwalking.InAutoAttackRange(m)) && m.HealthPercent <= 50 &&
                                        (_lastAfterFarmTarget == null || _lastAfterFarmTarget.NetworkId != m.NetworkId) &&
                                        (_lastBeforeFarmTarget == null || _lastBeforeFarmTarget.NetworkId != m.NetworkId))
                                .ToList();
                        if (minions.Any())
                        {
                            foreach (var minion in minions)
                            {
                                var health = HealthPrediction.GetHealthPrediction(
                                    minion, (int) (Q.ArrivalTime(minion) * 1000));
                                if (health > 0 && Math.Abs(health - minion.Health) > 10 &&
                                    Q.GetDamage(minion) * 0.85f > health)
                                {
                                    if (Q.CastOnUnit(minion))
                                    {
                                        _lastQKillableTarget = minion;
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    Orbwalking.PreventStuttering(HasQBuff());
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private bool IsSpellUpgraded(Spell spell)
        {
            try
            {
                return
                    Player.Buffs.Select(b => b.Name.ToLower())
                        .Where(b => b.StartsWith("viktor") && b.EndsWith("aug"))
                        .Any(
                            b =>
                                spell.Slot == SpellSlot.R
                                    ? b.Contains("qwe")
                                    : b.Contains(spell.Slot.ToString().ToLower()));
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return false;
        }

        private void OnGameObjectCreate(GameObject sender, EventArgs args)
        {
            try
            {
                if (sender.Type == GameObjectType.obj_GeneralParticleEmitter &&
                    sender.Name.Equals("Viktor_ChaosStorm_green.troy", StringComparison.OrdinalIgnoreCase))
                {
                    _rObject = sender;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnOrbwalkingBeforeAttack(Orbwalking.BeforeAttackEventArgs args)
        {
            try
            {
                if (HasQBuff())
                {
                    if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Combo)
                    {
                        if ((_rObject == null || !_rObject.IsValid) && R.IsReady() && UltimateManager.Combo() &&
                            R.Instance.Name.Equals("ViktorChaosStorm", StringComparison.OrdinalIgnoreCase) &&
                            GameObjects.EnemyHeroes.Any(Orbwalking.InAutoAttackRange) &&
                            (RLogicDuel(true, Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady(), true) ||
                             GameObjects.EnemyHeroes.Where(e => e.IsValidTarget(R.Range + R.Width))
                                 .Any(
                                     e =>
                                         RLogic(
                                             e, Menu.Item(Menu.Name + ".ultimate.combo.min").GetValue<Slider>().Value,
                                             Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady(),
                                             Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady(), true))))
                        {
                            args.Process = false;
                            return;
                        }
                    }
                    if (!(args.Target is Obj_AI_Hero))
                    {
                        var targets = TargetSelector.GetTargets(Player.AttackRange + Player.BoundingRadius * 3f);
                        if (targets != null && targets.Any())
                        {
                            var hero = targets.FirstOrDefault(Orbwalking.InAutoAttackRange);
                            if (hero != null)
                            {
                                Orbwalker.ForceTarget(hero);
                                args.Process = false;
                            }
                            else if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Mixed)
                            {
                                if (
                                    targets.Any(
                                        t =>
                                            t.Distance(Player) <
                                            (Player.BoundingRadius + t.BoundingRadius + Player.AttackRange) *
                                            (IsSpellUpgraded(Q) ? 1.4f : 1.2f)))
                                {
                                    args.Process = false;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Mixed &&
                        !Menu.Item(Menu.Name + ".harass.auto-attack").GetValue<bool>())
                    {
                        args.Process = false;
                        return;
                    }
                    if ((args.Target is Obj_AI_Hero) &&
                        (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Combo ||
                         Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Mixed) &&
                        (Q.IsReady() && Player.Mana >= Q.Instance.ManaCost ||
                         E.IsReady() && Player.Mana >= E.Instance.ManaCost))
                    {
                        args.Process = false;
                    }
                }
                if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LastHit ||
                    Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LaneClear)
                {
                    var minion = args.Target as Obj_AI_Minion;
                    if (minion != null &&
                        HealthPrediction.LaneClearHealthPrediction(
                            minion, (int) (Player.AttackDelay * 1000), Game.Ping / 2) <
                        Player.GetAutoAttackDamage(minion))
                    {
                        _lastBeforeFarmTarget = minion;
                    }
                    if (_lastQKillableTarget != null && _lastQKillableTarget.NetworkId == args.Target.NetworkId)
                    {
                        args.Process = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnOrbwalkingAfterAttack(AttackableUnit unit, AttackableUnit target)
        {
            _lastAutoAttack = Game.Time;
            if (Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LastHit ||
                Orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LaneClear)
            {
                var bTarget = unit as Obj_AI_Base;
                if (bTarget != null)
                {
                    _lastAfterFarmTarget = bTarget;
                }
            }
            Orbwalker.ForceTarget(null);
        }

        private void OnUnitDash(Obj_AI_Base sender, Dash.DashItem args)
        {
            try
            {
                var hero = sender as Obj_AI_Hero;
                if (!sender.IsEnemy || hero == null)
                {
                    return;
                }
                if (HeroListManager.Check("w-gapcloser", hero))
                {
                    if (args.EndPos.Distance(Player.Position) < W.Range)
                    {
                        W.Cast(args.EndPos);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnInterruptableTarget(Obj_AI_Hero sender, Interrupter2.InterruptableTargetEventArgs args)
        {
            try
            {
                if (sender.IsEnemy && args.DangerLevel == Interrupter2.DangerLevel.High &&
                    UltimateManager.Interrupt(sender))
                {
                    Utility.DelayAction.Add(DelayManager.Get("ultimate-interrupt-delay"), () => R.Cast(sender.Position));
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private void OnEnemyGapcloser(ActiveGapcloser args)
        {
            try
            {
                if (!args.Sender.IsEnemy)
                {
                    return;
                }
                if (HeroListManager.Check("w-gapcloser", args.Sender))
                {
                    if (args.End.Distance(Player.Position) < W.Range)
                    {
                        W.Cast(args.End);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private bool HasQBuff()
        {
            return Player.HasBuff("viktorpowertransferreturn");
        }

        protected override void Combo()
        {
            var q = Menu.Item(Menu.Name + ".combo.q").GetValue<bool>() && Q.IsReady();
            var w = Menu.Item(Menu.Name + ".combo.w").GetValue<bool>() && W.IsReady();
            var e = Menu.Item(Menu.Name + ".combo.e").GetValue<bool>() && E.IsReady();
            var r = UltimateManager.Combo() && R.IsReady();

            var qCasted = false;
            if (e)
            {
                var target = TargetSelector.GetTarget((MaxERange + E.Width) * 1.1f, E.DamageType);
                if (target != null)
                {
                    ELogic(target, GameObjects.EnemyHeroes.ToList(), E.GetHitChance("combo"));
                }
            }
            if (q)
            {
                var target = TargetSelector.GetTarget(Q.Range, Q.DamageType);
                if (target != null)
                {
                    qCasted = Q.CastOnUnit(target);
                }
            }
            if (w)
            {
                var target = TargetSelector.GetTarget(W);
                if (target != null)
                {
                    WLogic(target, W.GetHitChance("combo"));
                }
            }
            if (r)
            {
                var target = TargetSelector.GetTarget(R);
                if (target != null && (HasQBuff() || (qCasted || !q || !Q.IsReady()) || R.IsKillable(target)) &&
                    !RLogic(target, Menu.Item(Menu.Name + ".ultimate.combo.min").GetValue<Slider>().Value, q, e))
                {
                    if (Menu.Item(Menu.Name + ".ultimate.combo.duel").GetValue<bool>())
                    {
                        RLogicDuel(q, e);
                    }
                }
            }
            var rTarget = TargetSelector.GetTarget(R);
            if (rTarget != null && CalcComboDamage(rTarget, q, e, r) > rTarget.Health)
            {
                ItemManager.UseComboItems(rTarget);
                SummonerManager.UseComboSummoners(rTarget);
            }
        }

        protected override void Harass()
        {
            if (!ManaManager.Check("harass"))
            {
                return;
            }

            if (Menu.Item(Menu.Name + ".harass.e").GetValue<bool>())
            {
                var target = TargetSelector.GetTarget((MaxERange + E.Width) * 1.1f, E.DamageType);
                if (target != null)
                {
                    ELogic(target, GameObjects.EnemyHeroes.ToList(), E.GetHitChance("harass"));
                }
            }
            if (Menu.Item(Menu.Name + ".harass.q").GetValue<bool>())
            {
                var target = TargetSelector.GetTarget(Q.Range, Q.DamageType);
                if (target != null)
                {
                    Q.CastOnUnit(target);
                }
            }
        }

        private float CalcComboDamage(Obj_AI_Hero target, bool q, bool e, bool r)
        {
            try
            {
                if (target == null)
                {
                    return 0;
                }
                var damage = 0f;
                if (HasQBuff() && Orbwalking.InAutoAttackRange(target))
                {
                    damage += CalcPassiveDamage(target);
                }
                if (q && Q.IsReady() && target.IsValidTarget(Q.Range))
                {
                    damage += Q.GetDamage(target);
                    if (Orbwalking.InAutoAttackRange(target))
                    {
                        damage += CalcPassiveDamage(target);
                    }
                }
                if (e && E.IsReady() && target.IsValidTarget(MaxERange))
                {
                    damage += E.GetDamage(target);
                }
                if (r && R.IsReady() && target.IsValidTarget(R.Range + R.Width))
                {
                    damage += R.GetDamage(target);

                    int stacks;
                    if (!IsSpellUpgraded(R))
                    {
                        stacks = target.IsNearTurret(500f) ? 3 : 10;
                        var endTimes =
                            target.Buffs.Where(
                                t =>
                                    t.Type == BuffType.Charm || t.Type == BuffType.Snare || t.Type == BuffType.Knockup ||
                                    t.Type == BuffType.Polymorph || t.Type == BuffType.Fear || t.Type == BuffType.Taunt ||
                                    t.Type == BuffType.Stun).Select(t => t.EndTime).ToList();
                        if (endTimes.Any())
                        {
                            var max = endTimes.Max();
                            if (max - Game.Time > 0.5f)
                            {
                                stacks = 14;
                            }
                        }
                    }
                    else
                    {
                        stacks = 13;
                    }

                    damage += (R.GetDamage(target, 1) * stacks);
                }
                damage += ItemManager.CalculateComboDamage(target);
                damage += SummonerManager.CalculateComboDamage(target);
                return damage;
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return 0;
        }

        private float CalcPassiveDamage(Obj_AI_Base target)
        {
            try
            {
                return
                    (float)
                        Player.CalcDamage(
                            target, Damage.DamageType.Magical,
                            (new[] { 20, 25, 30, 35, 40, 45, 50, 55, 60, 70, 80, 90, 110, 130, 150, 170, 190, 210 }[
                                Player.Level - 1] + Player.TotalMagicalDamage * 0.5f + Player.TotalAttackDamage)) *
                    0.98f;
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return 0;
        }

        private void RFollowLogic()
        {
            try
            {
                if (_lastRMoveCommand + RMoveInterval > Environment.TickCount)
                {
                    return;
                }

                _lastRMoveCommand = Environment.TickCount;

                if (!R.Instance.Name.Equals("ViktorChaosStorm", StringComparison.OrdinalIgnoreCase) && _rObject != null &&
                    _rObject.IsValid)
                {
                    var pos = BestRFollowLocation(_rObject.Position);
                    if (!pos.Equals(Vector3.Zero))
                    {
                        Player.Spellbook.CastSpell(SpellSlot.R, pos);
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private bool RLogicDuel(bool q, bool e, bool simulated = false)
        {
            try
            {
                if (!R.Instance.Name.Equals("ViktorChaosStorm", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (
                    GameObjects.EnemyHeroes.Where(t => UltimateManager.CheckDuel(t, CalcComboDamage(t, q, e, true)))
                        .Any(t => RLogic(t, 1, q, e, simulated)))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return false;
        }

        private bool RLogic(Obj_AI_Hero target, int min, bool q, bool e, bool simulated = false, string mode = "combo")
        {
            try
            {
                if (!R.Instance.Name.Equals("ViktorChaosStorm", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                var pred = CPrediction.Circle(R, target, HitChance.High, false);
                if (pred.TotalHits > 0 &&
                    UltimateManager.Check(mode, min, pred.Hits, hero => CalcComboDamage(hero, q, e, true)))
                {
                    if (!simulated)
                    {
                        R.Cast(pred.CastPosition);
                        var aaTarget =
                            TargetSelector.GetTargets(Player.AttackRange + Player.BoundingRadius * 3f)
                                .FirstOrDefault(Orbwalking.InAutoAttackRange);
                        if (aaTarget != null)
                        {
                            Orbwalker.ForceTarget(aaTarget);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return false;
        }

        private void WLogic(Obj_AI_Hero target, HitChance hitChance)
        {
            try
            {
                var pred = W.GetPrediction(target, true);
                if (pred.Hitchance >= hitChance)
                {
                    W.Cast(pred.CastPosition);
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
        }

        private bool ELogic(Obj_AI_Hero target, List<Obj_AI_Hero> targets, HitChance hitChance, int minHits = 1)
        {
            return ELogic(
                target, targets.Select(t => t as Obj_AI_Base).Where(t => t != null).ToList(), hitChance, minHits);
        }

        private bool ELogic(Obj_AI_Hero mainTarget,
            List<Obj_AI_Base> targets,
            HitChance hitChance,
            int minHits,
            float overrideExtendedDistance = -1)
        {
            try
            {
                var input = new PredictionInput
                {
                    Range = ELength,
                    Delay = E.Delay,
                    Radius = E.Width,
                    Speed = E.Speed,
                    Type = E.Type
                };
                var input2 = new PredictionInput
                {
                    Range = MaxERange,
                    Delay = E.Delay,
                    Radius = E.Width,
                    Speed = E.Speed,
                    Type = E.Type
                };
                var startPos = Vector3.Zero;
                var endPos = Vector3.Zero;
                var hits = 0;
                targets = targets.Where(t => t.IsValidTarget(MaxERange + E.Width * 1.1f)).ToList();
                var targetCount = targets.Count;

                foreach (var target in targets)
                {
                    bool containsTarget;
                    var lTarget = target;
                    if (target.Distance(Player.Position) <= E.Range)
                    {
                        containsTarget = mainTarget == null || lTarget.NetworkId == mainTarget.NetworkId;
                        var cCastPos = target.Position;
                        foreach (var t in targets.Where(t => t.NetworkId != lTarget.NetworkId))
                        {
                            var count = 1;
                            var cTarget = t;
                            input.Unit = t;
                            input.From = cCastPos;
                            input.RangeCheckFrom = cCastPos;
                            var pred = Prediction.GetPrediction(input);
                            if (pred.Hitchance >= (hitChance - 1))
                            {
                                count++;
                                if (!containsTarget)
                                {
                                    containsTarget = t.NetworkId == mainTarget.NetworkId;
                                }
                                var rect = new Geometry.Polygon.Rectangle(
                                    cCastPos.To2D(), cCastPos.Extend(pred.CastPosition, ELength).To2D(), E.Width);
                                foreach (var c in
                                    targets.Where(
                                        c => c.NetworkId != cTarget.NetworkId && c.NetworkId != lTarget.NetworkId))
                                {
                                    input.Unit = c;
                                    var cPredPos = c.Type == GameObjectType.obj_AI_Minion
                                        ? c.Position
                                        : Prediction.GetPrediction(input).UnitPosition;
                                    if (
                                        new Geometry.Polygon.Circle(
                                            cPredPos,
                                            (c.Type == GameObjectType.obj_AI_Minion && c.IsMoving
                                                ? (c.BoundingRadius / 2f)
                                                : (c.BoundingRadius) * 0.9f)).Points.Any(p => rect.IsInside(p)))
                                    {
                                        count++;
                                        if (!containsTarget && c.NetworkId == mainTarget.NetworkId)
                                        {
                                            containsTarget = true;
                                        }
                                    }
                                }
                                if (count > hits && containsTarget)
                                {
                                    hits = count;
                                    startPos = cCastPos;
                                    endPos = cCastPos.Extend(pred.CastPosition, ELength);
                                    if (hits == targetCount)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        if (endPos.Equals(Vector3.Zero) && containsTarget)
                        {
                            startPos = target.IsFacing(Player) && IsSpellUpgraded(E)
                                ? Player.Position.Extend(cCastPos, Player.Distance(cCastPos) - (ELength / 10f))
                                : cCastPos;
                            endPos = Player.Position.Extend(cCastPos, ELength);
                            hits = 1;
                        }
                    }
                    else
                    {
                        input2.Unit = lTarget;
                        var castPos = Prediction.GetPrediction(input2).CastPosition;
                        var sCastPos = Player.Position.Extend(castPos, E.Range);

                        var extDist = overrideExtendedDistance > 0 ? overrideExtendedDistance : (ELength / 4f);
                        var circle =
                            new Geometry.Polygon.Circle(Player.Position, sCastPos.Distance(Player.Position), 45).Points
                                .Where(p => p.Distance(sCastPos) < extDist).OrderBy(p => p.Distance(lTarget));
                        foreach (var point in circle)
                        {
                            input2.From = point.To3D();
                            input2.RangeCheckFrom = point.To3D();
                            input2.Range = ELength;
                            var pred2 = Prediction.GetPrediction(input2);
                            if (pred2.Hitchance >= hitChance)
                            {
                                containsTarget = mainTarget == null || lTarget.NetworkId == mainTarget.NetworkId;
                                var count = 1;
                                var rect = new Geometry.Polygon.Rectangle(
                                    point, point.To3D().Extend(pred2.CastPosition, ELength).To2D(), E.Width);
                                foreach (var c in targets.Where(t => t.NetworkId != lTarget.NetworkId))
                                {
                                    input2.Unit = c;
                                    var cPredPos = c.Type == GameObjectType.obj_AI_Minion
                                        ? c.Position
                                        : Prediction.GetPrediction(input2).UnitPosition;
                                    if (
                                        new Geometry.Polygon.Circle(
                                            cPredPos,
                                            (c.Type == GameObjectType.obj_AI_Minion && c.IsMoving
                                                ? (c.BoundingRadius / 2f)
                                                : (c.BoundingRadius) * 0.9f)).Points.Any(p => rect.IsInside(p)))
                                    {
                                        count++;
                                        if (!containsTarget && c.NetworkId == mainTarget.NetworkId)
                                        {
                                            containsTarget = true;
                                        }
                                    }
                                }
                                if (count > hits && containsTarget ||
                                    count == hits && containsTarget && mainTarget != null &&
                                    point.Distance(mainTarget.Position) < startPos.Distance(mainTarget.Position))
                                {
                                    hits = count;
                                    startPos = point.To3D();
                                    endPos = startPos.Extend(pred2.CastPosition, ELength);
                                    if (hits == targetCount)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (hits == targetCount)
                    {
                        break;
                    }
                }
                if (hits >= minHits && !startPos.Equals(Vector3.Zero) && !endPos.Equals(Vector3.Zero))
                {
                    E.Cast(startPos, endPos);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return false;
        }

        protected override void LaneClear()
        {
            if (Menu.Item(Menu.Name + ".lane-clear.e").GetValue<bool>() && E.IsReady() &&
                ManaManager.Check("lane-clear-e"))
            {
                var minions = MinionManager.GetMinions(
                    MaxERange * 1.5f, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.MaxHealth);
                var minHits = minions.Any(m => m.Team == GameObjectTeam.Neutral)
                    ? 1
                    : Menu.Item(Menu.Name + ".lane-clear.e-min").GetValue<Slider>().Value;

                if (minions.Count >= minHits)
                {
                    ELogic(null, (minions.Concat(GameObjects.EnemyHeroes)).ToList(), HitChance.High, minHits);
                }
            }
            if (Menu.Item(Menu.Name + ".lane-clear.q").GetValue<bool>() && Q.IsReady() &&
                ManaManager.Check("lane-clear-q"))
            {
                var minion =
                    MinionManager.GetMinions(Q.Range, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.MaxHealth)
                        .FirstOrDefault(m => m.Health < Q.GetDamage(m) || m.Health * 2 > Q.GetDamage(m));
                if (minion != null)
                {
                    Casting.TargetSkill(minion, Q);
                }
            }
        }

        protected override void Flee()
        {
            if (Menu.Item(Menu.Name + ".flee.w").GetValue<bool>() && W.IsReady())
            {
                var near =
                    GameObjects.EnemyHeroes.Where(e => W.CanCast(e))
                        .OrderBy(e => e.Distance(Player.Position))
                        .FirstOrDefault();
                if (near != null)
                {
                    Casting.SkillShot(near, W, W.GetHitChance("combo"));
                }
            }
            if (Menu.Item(Menu.Name + ".flee.q-upgraded").GetValue<bool>() && Q.IsReady() && IsSpellUpgraded(Q))
            {
                var near =
                    GameObjects.EnemyHeroes.Where(e => Q.CanCast(e))
                        .OrderBy(e => e.Distance(Player.Position))
                        .FirstOrDefault();
                if (near != null)
                {
                    Casting.TargetSkill(near, Q);
                }
                else
                {
                    var mobs = MinionManager.GetMinions(Q.Range, MinionTypes.All, MinionTeam.NotAlly);
                    if (mobs.Any())
                    {
                        Casting.TargetSkill(mobs.First(), Q);
                    }
                }
            }
        }

        protected override void Killsteal()
        {
            if (Menu.Item(Menu.Name + ".killsteal.q-aa").GetValue<bool>() && Q.IsReady())
            {
                var target =
                    GameObjects.EnemyHeroes.FirstOrDefault(t => t.IsValidTarget() && Orbwalking.InAutoAttackRange(t));
                if (target != null)
                {
                    var damage = CalcPassiveDamage(target) + Q.GetDamage(target);
                    if (damage - 10 > target.Health)
                    {
                        Casting.TargetSkill(target, Q);
                        Orbwalker.ForceTarget(target);
                    }
                }
            }
            if (Menu.Item(Menu.Name + ".killsteal.e").GetValue<bool>() && E.IsReady())
            {
                foreach (var target in
                    GameObjects.EnemyHeroes.Where(t => t.IsValidTarget() && t.Distance(Player) < MaxERange))
                {
                    var damage = E.GetDamage(target);
                    if (damage - 10 > target.Health)
                    {
                        if (ELogic(target, GameObjects.EnemyHeroes.ToList(), E.GetHitChance("combo")))
                        {
                            break;
                        }
                    }
                }
            }
        }

        private Vector3 BestRFollowLocation(Vector3 position)
        {
            try
            {
                var center = Vector2.Zero;
                float radius = -1;
                var count = 0;
                var moveDistance = -1f;
                var maxRelocation = IsSpellUpgraded(R) ? R.Width * 1.2f : R.Width * 0.8f;
                var targets = GameObjects.EnemyHeroes.Where(t => t.IsValidTarget(1500f)).ToList();
                var circle = new Geometry.Polygon.Circle(position, R.Width);
                if (targets.Any())
                {
                    var minDistance = targets.Any(t => circle.IsInside(t)) ? targets.Min(t => t.BoundingRadius) * 2 : 0;
                    var possibilities =
                        ListExtensions.ProduceEnumeration(targets.Select(t => t.Position.To2D()).ToList())
                            .Where(p => p.Count > 1)
                            .ToList();
                    if (possibilities.Any())
                    {
                        foreach (var possibility in possibilities)
                        {
                            var mec = MEC.GetMec(possibility);
                            var distance = position.Distance(mec.Center.To3D());
                            if (mec.Radius < R.Width && distance < maxRelocation && distance > minDistance)
                            {
                                if (possibility.Count > count ||
                                    possibility.Count == count && (mec.Radius < radius || distance < moveDistance))
                                {
                                    moveDistance = position.Distance(mec.Center.To3D());
                                    center = mec.Center;
                                    radius = mec.Radius;
                                    count = possibility.Count;
                                }
                            }
                        }
                        if (!center.Equals(Vector2.Zero))
                        {
                            return center.To3D();
                        }
                    }
                    var dTarget = targets.OrderBy(t => t.Distance(position)).FirstOrDefault();
                    if (dTarget != null && position.Distance(dTarget.Position) > minDistance)
                    {
                        return dTarget.Position;
                    }
                }
            }
            catch (Exception ex)
            {
                Global.Logger.AddItem(new LogItem(ex));
            }
            return Vector3.Zero;
        }
    }
}