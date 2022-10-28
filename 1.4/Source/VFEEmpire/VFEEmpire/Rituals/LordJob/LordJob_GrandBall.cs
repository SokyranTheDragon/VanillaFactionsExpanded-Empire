﻿using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace VFEEmpire
{
	//Todo list
	//Starting Positions before start dance so pawn offset will work
	// -Add to duty def most likely as seperate job rather then do a new transition. New transition if thats easier which it might be
	//No support for FX or music right now as not using LordJob_ritual ticking. Add if wanted.
	//Quest Part to actually arrive pawns and add to lord
	//Make sure leave ritual gizmo works, but only for colonists
	//Play Music Jobs
	//Need a check on open space for ball
	//I need to have a behavior even if I'm not using a precept I need roles which is impossible without
	

	public class LordJob_GrandBall : LordJob_Ritual
	{
		public Pawn leadNoble;
		public LocalTargetInfo target;
		public Thing shuttle;
		public string questEndedSignal;
		public bool danceStarted;
		public bool danceFinished;
		public RitualOutcomeEffectWorker_GrandBall outcome;
		public List<Pawn> colonistParticipants = new();
		public LordToil exitToil;
		public LordToil_GrandBall_Dance ballToil;
		public bool flip;
		public static readonly int duration = 9000;
		public Room ballRoom;
		public List<Pawn> nobles;

		private int ticksThisRotation;
		public List<Pawn> dancers = new();
		public Dictionary<Pawn, Pawn> leadPartner = new();
		private Dictionary<Pawn, List<Pawn>> dancedWith = new();
		private int stage = 0;
		private List<Pawn> tmpHasPartner = new();
		private Dictionary<Pawn, int> totalPresenceTmp = new();
		public static readonly string MemoCeremonyStarted = "CeremonyStarted";
		public LordJob_GrandBall()
		{
		}

		public LordJob_GrandBall(Pawn leadNoble, LocalTargetInfo targetinfo, Thing shuttle, string questEnded, Room ballRoom)
        {
            this.leadNoble = leadNoble;
            this.target = targetinfo;
            this.shuttle = shuttle;
            this.questEndedSignal = questEnded;
            this.ballRoom = ballRoom;
        }

        public bool AllArrived
        {
            get
            {
				var all = true;
				foreach (var pawn in nobles)
                {
					if (!PawnTagSet(pawn, "Arrived"))
                    {
						all = false;
						break;
					}
                }
				return all;
            }
        }
        public override bool AllowStartNewGatherings => !danceStarted || danceFinished;

		public virtual DanceStages Stage => danceStages[stage];
		public override IntVec3 Spot => target.Cell;

		public override string RitualLabel => "VIER.GrandBall.Label".Translate();

		public override StateGraph CreateGraph()
		{
			var graph = new StateGraph();
			outcome = (RitualOutcomeEffectWorker_GrandBall)InternalDefOf.VFEE_GrandBall_Outcome.GetInstance();

			var wait_ForSpawned = new LordToil_Wait();
			graph.AddToil(wait_ForSpawned);
			var moveToPlace = new LordToil_BestowingCeremony_MoveInPlace(Spot,leadNoble);
			graph.AddToil(moveToPlace);
			var wait_StartBall = new LordToil_GrandBall_Wait(leadNoble);
			graph.AddToil(wait_StartBall);
			var wait_PostBall = new LordToil_Wait();

			exitToil = new LordToil_EnterShuttleOrLeave(shuttle, Verse.AI.LocomotionUrgency.Walk, true, true);
			graph.AddToil(exitToil);
			ballToil = new LordToil_GrandBall_Dance(target.Cell);
			graph.AddToil(ballToil);
			//Transitions
			var removeColonists = new TransitionAction_Custom(() => lord.RemovePawns(colonistParticipants));

			var transition_Spawned = new Transition(wait_ForSpawned, moveToPlace);
			transition_Spawned.AddTrigger(new Trigger_Custom((TriggerSignal signal) => signal.type == TriggerSignalType.Tick && lord.ownedPawns.All(x=>x.Spawned)));
			graph.transitions.Add(transition_Spawned);

			var transition_Arrived = new Transition(moveToPlace, wait_StartBall);
			transition_Arrived.AddTrigger(new Trigger_Custom((TriggerSignal signal) => signal.type == TriggerSignalType.Tick && leadNoble.Position == spot));
			graph.transitions.Add(transition_Arrived);

			var transition_StartBall = new Transition(wait_StartBall, ballToil);
			transition_StartBall.AddTrigger(new Trigger_Memo(MemoCeremonyStarted));
			transition_StartBall.AddPostAction(new TransitionAction_Custom(() =>
			{
				QuestUtility.SendQuestTargetSignals(lord.questTags, "ritualStarted", lord.Named("SUBJECT"));
				StartDance();
			}
			));
			graph.transitions.Add(transition_StartBall);
			
			var transition_BallEnd = new Transition(ballToil, wait_PostBall);
			transition_BallEnd.AddPreAction(removeColonists);
			transition_BallEnd.AddTrigger(new Trigger_Memo("CeremonyDone"));
			transition_BallEnd.AddPostAction(new TransitionAction_Custom(() => QuestUtility.SendQuestTargetSignals(lord.questTags, "CeremonyDone", lord.Named("SUBJECT"))));
			graph.transitions.Add(transition_BallEnd);

			var transition_BallInterupted = new Transition(ballToil, exitToil);
			transition_BallInterupted.AddPreAction(removeColonists);
			transition_BallInterupted.AddPreAction(new TransitionAction_Custom(() =>
            {
				StopDance();
			}));
			transition_BallInterupted.AddTrigger(new Trigger_TickCondition(() =>
			{
				return ballRoom.PsychologicallyOutdoors || shuttle.Destroyed;
			}, 60));
			transition_BallInterupted.AddTrigger(new Trigger_PawnHarmed());
			transition_BallInterupted.AddTrigger(new Trigger_PawnLostViolently());
			transition_BallInterupted.AddTrigger(new Trigger_Signal(questEndedSignal));
			graph.transitions.Add(transition_BallInterupted);

			var transition_Leave = new Transition(wait_PostBall, exitToil);
			transition_Leave.AddPreAction(removeColonists);
			transition_Leave.AddTrigger(new Trigger_TicksPassed(600));
			graph.transitions.Add(transition_Leave);

			var transition_LeaveHostile = new Transition(moveToPlace, exitToil);
			transition_LeaveHostile.AddPreAction(removeColonists);
			transition_LeaveHostile.AddSource(wait_StartBall);
			transition_LeaveHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
			graph.transitions.Add(transition_LeaveHostile);

			var transition_Hurt = new Transition(moveToPlace, exitToil);
			transition_Hurt.AddPreAction(removeColonists);
			transition_Hurt.AddSource(wait_StartBall);
			transition_Hurt.AddTrigger(new Trigger_PawnHarmed());
			transition_Hurt.AddTrigger(new Trigger_PawnLostViolently());
			graph.transitions.Add(transition_Hurt);

			var transition_QuestEnd = new Transition(moveToPlace, exitToil);
			transition_QuestEnd.AddPreAction(removeColonists);
			transition_QuestEnd.AddSource(wait_StartBall);
			transition_QuestEnd.AddSource(wait_ForSpawned);
			transition_QuestEnd.AddTrigger(new Trigger_Signal(questEndedSignal));
			graph.transitions.Add(transition_QuestEnd);

			
			
			return graph;

		}

		
        public override void LordJobTick()
        {
			if (danceStarted && !danceFinished)
            {
				outcome.Tick(this, 1f);
				ticksThisRotation++;
				if (AllArrived || ticksThisRotation > 1000)
				{
					StageSwap();					
					perPawnTags.Clear();
				}
				ticksPassed++;
			}
			
        }
		public void SetPartners()
		{
			var pawns = nobles;
			leadPartner.Clear();
			tmpHasPartner.Clear();
			foreach (var pawn in nobles)
			{
				bool flag = false;
				List<Pawn> pastPartners = null;
				if (dancedWith.ContainsKey(pawn))
				{
					pastPartners = dancedWith[pawn];
					if (pastPartners.Count >= pawns.Except(pawn).Count())
                    {
						pastPartners.Clear();
					}
				}
				foreach (var partner in pawns.Except(tmpHasPartner).Except(pawn))
				{
					if ((pastPartners.NullOrEmpty() || !pastPartners.Contains(partner)) && !tmpHasPartner.Contains(partner))
                    {
						leadPartner.Add(pawn, partner);
						DancedWith(pawn, partner);
						DancedWith(partner, pawn);
						flag = true;
						break;
					}
                }
				if (flag)
                {
					tmpHasPartner.Add(pawn);
				}
            }

		}
		private void DancedWith(Pawn pawn, Pawn partner)
        {
			if (dancedWith.TryGetValue(pawn, out var partners))
            {
				partners.Add(partner);
            }
            else
            {
				dancedWith.Add(pawn, new List<Pawn> { partner });
            }
        }
		public void StartDance()
		{
			nobles = lord.ownedPawns.Where(x => x.royalty?.HasAnyTitleIn(Faction.OfEmpire) ?? false).ToList();
			danceStarted = true;
			InterruptDancers();
		}
		public void StopDance()
		{
			danceFinished = true;
			lord.ReceiveMemo("CeremonyFinished");
			foreach (KeyValuePair<Pawn, int> keyValuePair in ballToil.Data.presentForTicks)
			{
				if (keyValuePair.Key != null && !keyValuePair.Key.Dead)
				{
					if (!totalPresenceTmp.ContainsKey(keyValuePair.Key))
					{
						totalPresenceTmp.Add(keyValuePair.Key, keyValuePair.Value);
					}
					else
					{
						Dictionary<Pawn, int> dictionary = totalPresenceTmp;
						Pawn key = keyValuePair.Key;
						dictionary[key] += keyValuePair.Value;
					}
				}
			}
			totalPresenceTmp.RemoveAll((tp) => tp.Value < 2500);
			outcome.Apply(ticksPassed / duration, totalPresenceTmp, this);
			InterruptDancers();
		}
		public void StageSwap()
		{
			stage++;			
			if (stage > danceStages.Count)
			{
				if (ticksPassed > duration)
                {
					StopDance();
					return;
				}
				stage = 0;
				flip = !flip;
				SetPartners();
			}
			InterruptDancers();
		}
		public void InterruptDancers()
		{
			foreach (var pawn in nobles)
			{
				pawn.jobs.CheckForJobOverride();
			}
		}
		public virtual IntVec3 PawnOffset(Pawn pawn)
		{
			var cell = IntVec3.Invalid;
			switch (Stage)
			{
				case DanceStages.Up:
					cell = flip(IntVec3.North);
					break;
				case DanceStages.Down:
					cell = flip(IntVec3.South);
					break;
				case DanceStages.Left:
					cell = flip(IntVec3.West);
					break;
				case DanceStages.Right:
					cell = flip(IntVec3.East);
					break;
				case DanceStages.Rotate:
					var lead = LeadPawn(pawn);
					if (lead)
					{
						var partnerPos = Partner(pawn).Position;
						var pos = pawn.Position;
						if (pos.x > partnerPos.x)
						{
							cell = IntVec3.SouthWest;
						}
						else { cell = IntVec3.NorthEast; }

					}
					else { cell = IntVec3.Zero; }
					break;
				case DanceStages.Dip:
					cell = IntVec3.Zero;
					break;
			}

			return cell;
			IntVec3 flip(IntVec3 c)
			{
				if (this.flip)
				{
					c.x *= -1;
					c.z *= -1;
				}
				return c;
			}
		}
		public bool LeadPawn(Pawn pawn)
		{
			return leadPartner.ContainsKey(pawn);
		}

		public Pawn Partner(Pawn pawn)
		{
			var partner = leadPartner.TryGetValue(pawn);
			if (partner == null)
			{
				partner = leadPartner.FirstOrDefault(x=>x.Value == pawn).Key;
			}
			return partner;
		}

		private static readonly List<DanceStages> danceStages = new List<DanceStages>() { DanceStages.Up, DanceStages.Up, DanceStages.Rotate, DanceStages.Left, DanceStages.Left, DanceStages.Down, DanceStages.Rotate, DanceStages.Down, DanceStages.Right, DanceStages.Down, DanceStages.Right, DanceStages.Dip, DanceStages.Rotate };

	}
	public enum DanceStages
	{
		Up,
		Left,
		Down,
		Right,
		Rotate,
		Dip
	}
}
