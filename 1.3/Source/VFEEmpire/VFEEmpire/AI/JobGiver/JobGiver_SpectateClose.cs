﻿using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace VFEEmpire
{
    public class JobGiver_SpectateClose : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            PawnDuty duty = pawn.mindState.duty;
            if (duty == null)
            {
                return null;
            }
            IntVec3 spot;
            if (!TryFindSpot(pawn, duty, out spot))
            {
                return null;
            }
            IntVec3 centerCell = duty.spectateRect.CenterCell;
            Building edifice = spot.GetEdifice(pawn.Map);
            if (edifice != null && pawn.CanReserveSittableOrSpot(spot, false))
            {
                return JobMaker.MakeJob(JobDefOf.SpectateCeremony, spot, centerCell, edifice);
            }
            return JobMaker.MakeJob(JobDefOf.SpectateCeremony, spot, centerCell);        
        }
        protected virtual bool TryFindSpot(Pawn pawn, PawnDuty duty, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            Precept_Ritual ritual = null;
            LordJob_Ritual lordJob = pawn.GetLord()?.LordJob as LordJob_Ritual;
            if (lordJob == null)
            {
                return false;
            }
            var throne = lordJob.selectedTarget.Thing;            
            IntVec3 target = lordJob.Spot;
            var pawnThrone = RoyalTitleUtility.FindBestUsableThrone(pawn);
            if (pawnThrone != null && pawnThrone.GetRoom() == throne.GetRoom())
            {
                spot = pawnThrone.InteractionCell;
                return true;
            }

            if ((duty.spectateRectPreferredSide != SpectateRectSide.None && SpectatorCellFinder.TryFindSpectatorCellFor(pawn, duty.spectateRect, 
                pawn.Map, out spot, duty.spectateRectPreferredSide, 1, null, ritual, 
                new Func<IntVec3, Pawn, Map, bool>(RitualUtility.GoodSpectateCellForRitual))) 
                || SpectatorCellFinder.TryFindSpectatorCellFor(pawn, duty.spectateRect,pawn.Map, out spot, duty.spectateRectAllowedSides, 1, null, ritual,
                new Func<IntVec3, Pawn, Map, bool>(RitualUtility.GoodSpectateCellForRitual)))
            {
                return true;
            }
            if (CellFinder.TryFindRandomReachableCellNear(target, pawn.MapHeld, 3f, TraverseParms.For(pawn),
    (IntVec3 c) => c.GetRoom(pawn.MapHeld) == target.GetRoom(pawn.MapHeld) && pawn.CanReserveSittableOrSpot(c, false) && c != throne.InteractionCell, null, out spot, 999999))
            {
                return true;
            }
            Log.Warning("Failed to find a spectator spot for " + pawn);
            return false;
        }
    }
}
