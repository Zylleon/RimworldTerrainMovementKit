﻿using System;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;

namespace TerrainMovement
{
    [HarmonyPatch(typeof(Pawn_PathFollower), "PawnCanOccupy", new Type[] { typeof(IntVec3) })]
    public class TerrainAwarePawnCanOccupy
    {
        static void Postfix(ref bool __result, Pawn ___pawn, IntVec3 c)
        {
            if (__result)
            {
                __result = !___pawn.kindDef.UnreachableTerrainCheck(___pawn.Map.terrainGrid.TerrainAt(c));
            }
            
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(Pawn), typeof(IntVec3) })]
    public class TerrainAwareFollowerPatch
    {
        static bool Prefix(ref int __result, Pawn pawn, IntVec3 c)
        {
            int num;
            if (c.x == pawn.Position.x || c.z == pawn.Position.z)
            {
                num = pawn.TerrainAwareTicksPerMoveCardinal(c);
            }
            else
            {
                num = pawn.TerrainAwareTicksPerMoveDiagonal(c);
            }
            // Replace the calculated cost with one which is pawn / terrain aware
            //num += pawn.Map.pathGrid.CalculatedCostAt(c, perceivedStatic: false, pawn.Position);
            num += pawn.Map.pathGrid.TerrainCalculatedCostAt(pawn.Map, pawn, c, false, pawn.Position);
            // Rest of function is the same...
            Building edifice = c.GetEdifice(pawn.Map);
            if (edifice != null)
            {
                num += edifice.PathWalkCostFor(pawn);
            }
            if (num > 450)
            {
                num = 450;
            }
            if (pawn.CurJob != null)
            {
                Pawn locomotionUrgencySameAs = pawn.jobs.curDriver.locomotionUrgencySameAs;
                if (locomotionUrgencySameAs != null && locomotionUrgencySameAs != pawn && locomotionUrgencySameAs.Spawned)
                {
                    int num2 = 0;
                    // Call the prefix directly because the method we're patching is private
                    Prefix(ref num2, locomotionUrgencySameAs, c);
                    if (num < num2)
                    {
                        num = num2;
                    }
                }
                else
                {
                    switch (pawn.jobs.curJob.locomotionUrgency)
                    {
                        case LocomotionUrgency.Amble:
                            num *= 3;
                            if (num < 60)
                            {
                                num = 60;
                            }
                            break;
                        case LocomotionUrgency.Walk:
                            num *= 2;
                            if (num < 50)
                            {
                                num = 50;
                            }
                            break;
                        case LocomotionUrgency.Jog:
                            break;
                        case LocomotionUrgency.Sprint:
                            num = Mathf.RoundToInt((float)num * 0.75f);
                            break;
                    }
                }
            }
            __result = Mathf.Max(num, 1);
            return false;
        }
    }

    public static class PawnKindDefExtensions
    {
        // Provides an opportunity for other mods to manipulate terrain movement stats based on their mod extensions
        public static TerrainMovementPawnRestrictions LoadTerrainMovementPawnRestrictionsExtension(DefModExtension ext)
        {
            if (ext is TerrainMovementPawnRestrictions)
            {
                return ext as TerrainMovementPawnRestrictions;
            }
            return null;
        }

        public static bool UnreachableTerrainCheck(IEnumerable<DefModExtension> modExtensions, TerrainDef terrain)
        {
            if (modExtensions != null)
            {
                foreach (DefModExtension ext in modExtensions)
                {
                    TerrainMovementPawnRestrictions restrictions = LoadTerrainMovementPawnRestrictionsExtension(ext);
                    if (restrictions != null)
                    {
                        if (restrictions.stayOffTerrainTag != null && terrain.HasTag(restrictions.stayOffTerrainTag))
                        {
                            return true;
                        }
                        if (restrictions.stayOnTerrainTag != null && !terrain.HasTag(restrictions.stayOnTerrainTag))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public static bool UnreachableTerrainCheck(this PawnKindDef kind, TerrainDef terrain)
        {
            return UnreachableTerrainCheck(kind.race.modExtensions, terrain);
        }
    }

    public static class PawnExtensions
    {
        public static (StatDef moveStat, StatDef costStat) BestTerrainMovementStatDefs(this Pawn pawn, TerrainDef terrain)
        {
            (StatDef moveStat, StatDef costStat) bestStats = (null, null);
            float curSpeed = -1;
            foreach (var terrainStats in terrain.TerrainMovementStatDefs())
            {
                // Lazily calculate curSpeed for performance reasons
                if (bestStats.moveStat == null)
                {
                    bestStats = terrainStats;
                }
                else
                {
                    if (curSpeed < 0)
                    {
                        curSpeed = pawn.GetStatValue(bestStats.moveStat) / terrain.MovementCost(bestStats.costStat);
                    }
                    float newSpeed = pawn.GetStatValue(terrainStats.moveStat ?? StatDefOf.MoveSpeed) / terrain.MovementCost(terrainStats.costStat);
                    // Find highest movement statistic for this pawn
                    if (newSpeed >= curSpeed)
                    {
                        curSpeed = newSpeed;
                        bestStats = terrainStats;
                    }
                }
            }

            return bestStats;
        }

        public static StatDef TerrainMoveStat(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.BestTerrainMovementStatDefs(terrain).moveStat;
        }

        public static int TerrainMoveCost(this Pawn pawn, TerrainDef terrain)
        {
            return terrain.MovementCost(pawn.BestTerrainMovementStatDefs(terrain).costStat);
        }

        public static float TerrainSpeed(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.GetStatValue(pawn.TerrainMoveStat(terrain));
        }

        public static int TerrainAwareTicksPerMoveCardinal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(pawn.Map.terrainGrid.TerrainAt(loc), false);
        }

        public static int TerrainAwareTicksPerMoveDiagonal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(pawn.Map.terrainGrid.TerrainAt(loc), true);
        }

        public static int TerrainAwareTicksPerMoveCardinal(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.TerrainAwareTicksPerMove(terrain, false);
        }

        public static int TerrainAwareTicksPerMoveDiagonal(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.TerrainAwareTicksPerMove(terrain, true);
        }

        public static int TerrainAwareTicksPerMove(this Pawn pawn, TerrainDef terrain, bool diagonal)
        {
            float num = TerrainSpeed(pawn, terrain);
            // Rest of this is the same as vanilla function
            if (RestraintsUtility.InRestraints(pawn))
            {
                num *= 0.35f;
            }
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing.def.category == ThingCategory.Pawn)
            {
                num *= 0.6f;
            }
            float num2 = num / 60f;
            float num3;
            if (num2 == 0f)
            {
                num3 = 450f;
            }
            else
            {
                num3 = 1f / num2;
                if (pawn.Spawned && !pawn.Map.roofGrid.Roofed(pawn.Position))
                {
                    num3 /= pawn.Map.weatherManager.CurMoveSpeedMultiplier;
                }
                if (diagonal)
                {
                    num3 *= 1.41421f;
                }
            }
            int value = Mathf.RoundToInt(num3);
            return Mathf.Clamp(value, 1, 450);
        }
    }
}