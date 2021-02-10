﻿
using System;
using System.Collections.Generic;

namespace Assets.Scripts.Agents
{
    public class TrackingData
    {
        public bool HasData { get; set; }
        public bool HasCoarseData { get; set; }

        public Guid AvatarId { get; set; }
        public string Name { get; set; }
        public Vector3Double CoarseLocation { get; set; }
    }

    public class FriendObserver
    {
        [Flags]
        public enum ChangeType : UInt32
        {
            None   = 0,
            Add    = 1,
            Remove = 2,
            Online = 4,
            Powers = 8,

            All = 0xffffffff
        }

        public virtual void Changed(ChangeType changeType)
        {
        }
    }

    public class AvatarTracker
    {
        public static AvatarTracker Instance { get; } = new AvatarTracker();
        public static readonly float CoarseFrequency  = 2.2f;
        public static readonly float FindFrequency    = 29.7f; // This results in a database query, so cut these back
        public static readonly float OfflineSeconds   = FindFrequency + 8f;

        public Dictionary<Guid, Relationship> BuddyInfo = new Dictionary<Guid, Relationship>();

        protected TrackingData CurrentTrackingData;

        /// <summary>
        /// Used to make sure that no call to NotifyObservers will do anything if a previous call is still in progress.
        ///
        ///  TODO: This feels like a clunky way to do this.
        /// 
        /// </summary>
        private bool _isNotifyObservers = false;

        protected List<FriendObserver> Observers = new List<FriendObserver>();
        protected Dictionary<Guid, HashSet<FriendObserver>> ParticularFriendObserverMap { get; set; } = new Dictionary<Guid, HashSet<FriendObserver>>();

        protected FriendObserver.ChangeType ModifyMask { get; set; }
        protected HashSet<Guid> ChangedBuddyIds { get; set; } = new HashSet<Guid>();


        public void AddBuddyList(Dictionary<Guid, Relationship> list)
        {
            foreach (KeyValuePair<Guid, Relationship> kv in list)
            {
                Guid agentId = kv.Key;
                if (BuddyInfo.ContainsKey(agentId))
                {
                    Relationship existingRelationship = BuddyInfo[agentId];
                    Relationship newRelationship = kv.Value;
                    Logger.LogWarning($"!! Add buddy for existing buddy: {agentId}"
                                      + $" [{(existingRelationship.IsOnline ? "Online" : "Offline")} -> {(newRelationship.IsOnline ? "Online" : "Offline")}"
                                      + $", {existingRelationship.GrantToAgent} -> {newRelationship.GrantToAgent}"
                                      + $", {existingRelationship.GrantFromAgent} -> {newRelationship.GrantFromAgent}"
                                      + "]");
                }
                else
                {
                    BuddyInfo[agentId] = kv.Value;

                    //TODO: Do things with AvatarNameCache
                    //// pre-request name for notifications?
                    //LLAvatarName av_name;
                    //LLAvatarNameCache::get(agent_id, &av_name);

                    AddChangedMask (FriendObserver.ChangeType.Add, agentId);
                    
                    Logger.LogDebug($"Added buddy {agentId}, {(BuddyInfo[agentId].IsOnline ? "Online" : "Offline")}, TO: {BuddyInfo[agentId].GrantToAgent}, FROM: {BuddyInfo[agentId].GrantFromAgent}");
                }
            }
        }


        protected void AddChangedMask(FriendObserver.ChangeType changeType, Guid agentId)
        {
            ModifyMask |= changeType;
            if (agentId == Guid.Empty)
            {
                return;
            }

            ChangedBuddyIds.Add(agentId);
        }

        public void RegisterCallbacks()
        {
            //EventManager.Instance.OnFindAgentMessage += OnFindAgentMessage; //TODO: Create event for FindAgent
            EventManager.Instance.OnOnlineNotificationMessage += OnOnlineNotificationMessage;
            EventManager.Instance.OnOfflineNotificationMessage += OnOfflineNotificationMessage;
            //EventManager.Instance.OnTerminateFriendshipMessage += OnTerminateFriendshipMessage; //TODO: Create event for TerminateFriendship
            //EventManager.Instance.OnChangeUserRightsMessage += OnChangeUserRightsMessage; //TODO: Create event for ChangeUserRights
        }

        protected void OnOnlineNotificationMessage (OnlineNotificationMessage message)
        {
            Logger.LogDebug("LLAvatarTracker::processOnlineNotification()");
            ProcessNotify (message.Agents, true);
        }

        protected void OnOfflineNotificationMessage(OfflineNotificationMessage message)
        {
            Logger.LogDebug("LLAvatarTracker::processOfflineNotification()");
            ProcessNotify (message.Agents, false);
        }

        private void ProcessNotify(List<Guid> agents, bool isOnline)
        {
            int count = agents.Count;
            bool chatNotify = true; //TODO: Fetch from settings: gSavedSettings.getBOOL("ChatOnlineNotification");

            Logger.LogDebug($"Received {count} online notifications **** ");
            if (count <= 0)
            {
                return;
            }

            Guid trackingId;
            if (CurrentTrackingData != null)
            {
                trackingId = CurrentTrackingData.AvatarId;
            }
            for (int i = 0; i < count; i++)
            {
                Guid agentId = agents[i];
                Relationship info = GetBuddyInfo(agentId);
                if (info != null)
                {
                    SetBuddyOnline (agentId, isOnline);
                }
                else
                {
                    Logger.LogWarning($"Received online notification for unknown buddy: {agentId} is {(isOnline ? "ONLINE" : "OFFLINE")}");
                }

                if (trackingId == agentId)
                {
                    // we were tracking someone who went offline
                    DeleteTrackingData();
                }

                //TODO: Update online status in calling card:
                //// *TODO: get actual inventory id
                //gInventory.addChangedMask(LLInventoryObserver::CALLING_CARD, LLUUID::null);
            }
            if (chatNotify)
            {
                // Look up the name of this agent for the notification
                // TODO: LLAvatarNameCache::get(agent_id, boost::bind(&on_avatar_name_cache_notify, _1, _2, online, payload));
            }

            ModifyMask |= FriendObserver.ChangeType.Online;
            NotifyObservers();
            // TODO: Notify inventory observers: gInventory.notifyObservers();

        }

        protected Relationship GetBuddyInfo(Guid agentId)
        {
            return BuddyInfo.ContainsKey(agentId) ? BuddyInfo[agentId] : null;
        }

        protected void SetBuddyOnline(Guid agentId, bool isOnline)
        {
            if (BuddyInfo.ContainsKey(agentId) == false)
            {
                Logger.LogWarning($"!! No buddy info found for {agentId}, setting to {(isOnline ? "Online" : "Offline")}");
                return;
            }

            Relationship info = BuddyInfo[agentId];
            info.IsOnline = isOnline;
            AddChangedMask (FriendObserver.ChangeType.Online, agentId);
            Logger.LogDebug($"Set buddy {agentId} {(isOnline ? "Online" : "Offline")}");
        }

        protected void DeleteTrackingData()
        {
            CurrentTrackingData = null;
        }

        public void AddObserver(FriendObserver observer)
        {
            if (observer == null)
            {
                return;
            }
            Observers.Add(observer);
        }

        public void RemoveObserver(FriendObserver observer)
        {
            if (observer == null)
            {
                return;
            }

            Observers.Remove(observer);
        }

        protected void NotifyObservers()
        {
            if (_isNotifyObservers)
            {
                // Don't allow multiple calls.
                // new masks and ids will be processed later from idle.
                return;
            }
            _isNotifyObservers = true;

            foreach (FriendObserver observer in Observers)
            {
                observer.Changed(ModifyMask);
            }

            foreach (Guid buddyId in ChangedBuddyIds)
            {
                NotifyParticularFriendObservers(buddyId);
            }

            ModifyMask = FriendObserver.ChangeType.None;
            ChangedBuddyIds.Clear();
            _isNotifyObservers = false;
        }

        public void AddParticularFriendObserver (Guid buddy_id, FriendObserver observer)
        {
            if (buddy_id != Guid.Empty && observer != null)
            {
                if (ParticularFriendObserverMap.ContainsKey(buddy_id) == false)
                {
                    ParticularFriendObserverMap[buddy_id] = new HashSet<FriendObserver>();
                }
                ParticularFriendObserverMap[buddy_id].Add(observer);
            }
        }

        public void RemoveParticularFriendObserver (Guid buddy_id, FriendObserver observer)
        {
            if (buddy_id == Guid.Empty || observer == null)
            {
                return;
            }

            if (ParticularFriendObserverMap.ContainsKey(buddy_id) == false)
            {
                return;
            }

            ParticularFriendObserverMap[buddy_id].Remove(observer);

            // purge empty sets from the map
            if (ParticularFriendObserverMap[buddy_id].Count == 0)
            {
                ParticularFriendObserverMap.Remove(buddy_id);
            }
        }

        protected void NotifyParticularFriendObservers (Guid buddy_id)
        {
            if (ParticularFriendObserverMap.ContainsKey(buddy_id) == false)
            {
                return;
            }

            // Notify observers interested in buddy_id.
            foreach (FriendObserver observer in ParticularFriendObserverMap[buddy_id])
            {
                observer.Changed(ModifyMask);
            }
        }

    }
}
