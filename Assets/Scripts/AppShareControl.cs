﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HoloToolkit.Sharing;
using HoloToolkit.Sharing.Tests;
using HoloToolkit.Unity;
using HoloToolkit.Unity.SpatialMapping;
using UnityEngine;
using UnityEngine.VR.WSA.Persistence;
using UnityEngine.VR.WSA;
using UnityEngine.VR.WSA.Sharing;

public class AppShareControl : Singleton<AppShareControl>
{
    public bool IsSharingSessionConnected;
    public bool IsSharingManagerConnected;
    public bool IsAnchorConfigured;
    public bool IsAnchorLocated;
    public bool IsLocalAnchor;

    public bool KeepRoomAlive = true;                                   // Do not close Room/Session to maintain previous anchors
    public string RoomName = "ShareRoom";                               // Predefined Room  Name
    public long RoomID = 74656;                                         // Fixed RoomID to mainain room
    public uint MinTrustworthySerializedAnchorDataSize = 100000;        // Min Anchor Data Size (larger contains more feature detail)    

    [SerializeField] private TextMesh DebugText;    

    private WorldAnchorStore anchorStore;
    private RoomManager roomManager;
    private RoomManagerAdapter roomManagerListener;
    private Room currentRoom;
    private byte[] anchorDownloadRawBytes;
    private List<byte> exportingAnchorBytes = new List<byte>();
    private string exportingAnchorName;
    private WorldAnchorTransferBatch sharedAnchorInterface;
    private XString storedAnchorString;
    private string remoteAnchorName;


    private AnchorManagementState _previousTrackingState;
    private AnchorManagementState _currentState;
    public AnchorManagementState CurrentState
    {
        get { return _currentState; }
        set
        {            
            _currentState = value;
            Debug.LogFormat("ChangeState: {0}", _currentState.ToString());
            DebugDisplay(string.Format("\nChangeState: {0}", _currentState.ToString()));
        }
    }

    public enum AnchorManagementState
    {
        WaitingForAnchorStore,
        AnchorStoreReady,
        InitializeRoom,
        InitializingRoom,
        

        CreateLocalAnchor,
        CreatingLocalAnchor,
        ReadyToExportLocalAnchor,
        ExportingLocalAnchor,

        GetRemoteAnchor,
        GetRemoteAnchorStarting,
        RemoteAnchorDataRequest,
        RemoteAnchorDataReady,
        RemoteAnchorAttaching,
        RemoteAnchorAttached,
        RemoteAnchorAttachFailed,

        LocalAnchorExported,
        LocalAnchorExportFailed,

        CachedAnchorAttached,

        Ready,
        AnchorPlacementStart,
        AnchorPlacement,
        AnchorPlacementDone
    }

    private void DebugDisplay(string msg)
    {
        if (DebugText != null)
        {
            DebugText.text += msg;
        }
    }

    private void SessionsTracker_ServerConnected()
    {
        IsSharingSessionConnected = true;
        DebugDisplay(string.Format("\nSharingServer-SessionsTracker: {0}", IsSharingSessionConnected.ToString()));
    }

    private void SessionsTracker_ServerDisconnected()
    {
        IsSharingSessionConnected = false;        
        DebugDisplay(string.Format("\nnSharingServer-SessionsTracker: {0}", IsSharingSessionConnected.ToString()));
    }

    private void AnchorStoreReady(WorldAnchorStore store)
    {
        Debug.Log("WorldAnchorStore READY");
        anchorStore = store;
        CurrentState = AnchorManagementState.AnchorStoreReady;

        if (!KeepRoomAlive)
        {
            anchorStore.Clear();
        }
    }

    private void ResetState()
    {
#if UNITY_WSA && !UNITY_EDITOR        
        IsLocalAnchor = false;
        IsAnchorConfigured = false;

        if (anchorStore != null)
        {            
            CurrentState = AnchorManagementState.AnchorStoreReady;
        }
        else
        {
            // Re-Initialize
            CurrentState = AnchorManagementState.WaitingForAnchorStore;            
        }
#else
        CurrentState = AnchorManagementState.AnchorStoreReady;
#endif
    }

    private void RoomManagerCallbacks_AnchorsChanged(Room room)
    {
        Debug.LogFormat("Anchor Manager: Anchors in room {0} changed", room.GetName());
        DebugDisplay(string.Format("\nAnchors in ({0}) CHANGED!", room.GetName()));        

        // if we're currently in the room where the anchors changed
        if (currentRoom == room)
        {
            ResetState();
        }
    }

    private void MakeAnchorDataRequest()
    {        
        if (storedAnchorString != null && roomManager.DownloadAnchor(currentRoom, storedAnchorString))
        {            
            CurrentState = AnchorManagementState.RemoteAnchorDataRequest;
        }
        else
        {
            Debug.LogError("Anchor Manager: Couldn't make the download request.");            
            DebugDisplay(string.Format("\nCouldn't make the download request: {0}", storedAnchorString.GetString()));
            CurrentState = AnchorManagementState.RemoteAnchorAttachFailed;
        }
    }

    private void RoomManagerListener_AnchorsDownloaded(bool successful, AnchorDownloadRequest request, XString failureReason)
    {
        // If we downloaded anchor data successfully we should import the data.
        if (successful)
        {
            int datasize = request.GetDataSize();
            Debug.LogFormat("Remote Anchor Download Size: {0} bytes.", datasize.ToString());
            DebugDisplay(string.Format("\nRemote Anchor Download Size: {0} bytes.", datasize.ToString()));

            anchorDownloadRawBytes = new byte[datasize];
            request.GetData(anchorDownloadRawBytes, datasize);            
            CurrentState = AnchorManagementState.RemoteAnchorDataReady;
        }
        else
        {
            Debug.LogFormat("\nAnchor Download Failed " + failureReason);
            DebugDisplay(string.Format("\nAnchor Download Failed " + failureReason));
            
#if UNITY_WSA && !UNITY_EDITOR
            MakeAnchorDataRequest();
#endif
        }
    }

    private void RoomManagerListener_AnchorUploaded(bool successful, XString failureReason)
    {
        if (successful)
        {
            Debug.Log("Anchor Manager: Sucessfully Exported Local Anchor");
            DebugDisplay("\nSucessfully Exported Local Anchor");
            CurrentState = AnchorManagementState.LocalAnchorExported;

            // Notify other users of new uploaded anchor
            CustomMessages2.Instance.SendNewAnchorNotification(exportingAnchorName);
        }
        else
        {            
            DebugDisplay(string.Format("\nAnchor Export failed " + failureReason));
            Debug.LogError("Anchor Manager: Anchor Export failed " + failureReason);            
            CurrentState = AnchorManagementState.LocalAnchorExportFailed;
        }        
    }

    private void CurrentUserJoinedSession(Session session)
    {
        if (SharingStage.Instance.Manager.GetLocalUser().IsValid())
        {
            IsSharingSessionConnected = true;
        }
        else
        {
            Debug.LogWarning("Unable to get local user on session joined");
        }
    }

    private void CurrentUserLeftSession(Session session)
    {        
        IsSharingSessionConnected = false;

        // Reset the state so that we join a new room when we eventually rejoin a session
        ResetState();
    }

    private void SharingManagerConnected(object sender = null, EventArgs e = null)
    {
        SharingStage.Instance.SharingManagerConnected -= SharingManagerConnected;        
        DebugDisplay("\nSharingManagerConnected() Event");
        Debug.Log("SharingManagerConnected() Event");

        // Setup the room manager callbacks.
        roomManager = SharingStage.Instance.Manager.GetRoomManager();
        roomManagerListener = new RoomManagerAdapter();

        roomManagerListener.AnchorsChangedEvent += RoomManagerCallbacks_AnchorsChanged;
        roomManagerListener.AnchorsDownloadedEvent += RoomManagerListener_AnchorsDownloaded;
        roomManagerListener.AnchorUploadedEvent += RoomManagerListener_AnchorUploaded;
        roomManager.AddListener(roomManagerListener);

        // We will register for session joined and left to indicate when the sharing service
        // is ready for us to make room related requests.
        SharingStage.Instance.SessionsTracker.CurrentUserJoined += CurrentUserJoinedSession;
        SharingStage.Instance.SessionsTracker.CurrentUserLeft += CurrentUserLeftSession;

        // Setup Sharing Service Connection        
        SharingStage.Instance.SessionsTracker.ServerConnected += SessionsTracker_ServerConnected;
        SharingStage.Instance.SessionsTracker.ServerDisconnected += SessionsTracker_ServerDisconnected;

        IsSharingManagerConnected = true;        
    }

    private void NewAnchorPlacementMessage(NetworkInMessage msg)
    {
        // TODO: Load New Anchor
        remoteAnchorName = CustomMessages2.Instance.ReadString(msg);
        DebugDisplay(string.Format(">>> NEW ANCHOR MESSAGE: {0}", remoteAnchorName));
        ClearPlacementObjectAnchors();
        CurrentState = AnchorManagementState.GetRemoteAnchor;
    }

    void Start()
    {
        // Setup Anchor System
        remoteAnchorName = string.Empty;
        CurrentState = AnchorManagementState.WaitingForAnchorStore;
        WorldAnchorStore.GetAsync(AnchorStoreReady);

        // Setup Message Handlers
        CustomMessages2.Instance.MessageHandlers[CustomMessages2.AppShareControlMessageID.NewAnchorPlacement] = NewAnchorPlacementMessage;

        // Setup Connection Immediately if available, otherwise handle as an event
        if (SharingStage.Instance.IsConnected)
        {
            SharingManagerConnected();
        }
        else
        {
            SharingStage.Instance.SharingManagerConnected += SharingManagerConnected;            
        }        
    }

    private static bool ShouldLocalUserCreateRoom
    {
        get
        {
            if (SharingStage.Instance == null || SharingStage.Instance.SessionUsersTracker == null)
            {
                return false;
            }

            long localUserId;
            using (User localUser = SharingStage.Instance.Manager.GetLocalUser())
            {
                localUserId = localUser.GetID();                
            }

            for (int i = 0; i < SharingStage.Instance.SessionUsersTracker.CurrentUsers.Count; i++)
            {
                if (SharingStage.Instance.SessionUsersTracker.CurrentUsers[i].GetID() < localUserId)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private IEnumerator InitializeRoom()
    {
        CurrentState = AnchorManagementState.InitializingRoom;
        
        // First check if there is a current room
        currentRoom = roomManager.GetCurrentRoom();
        while (currentRoom == null)
        {
            // If we have a room, we'll join the first room we see.
            // If we are the user with the lowest user ID, we will create the room.
            // Otherwise we will wait for the room to be created.
            if (roomManager.GetRoomCount() == 0)
            {
                if (ShouldLocalUserCreateRoom)
                {
                    Debug.LogFormat("InitializeRoom() - User Creating Room: {0}", RoomName);
                    DebugDisplay(string.Format("\nInitializeRoom() - User Creating Room: {0}",RoomName));

                    // To keep anchors alive even if all users have left the session ...
                    // Pass in true instead of false in CreateRoom.
                    currentRoom = roomManager.CreateRoom(new XString(RoomName), RoomID, KeepRoomAlive);
                }
            }
            else
            {
                // Look through the existing rooms and join the one that matches the room name provided.
                int roomCount = roomManager.GetRoomCount();
                for (int i = 0; i < roomCount; i++)
                {
                    Room room = roomManager.GetRoom(i);
                    if (room.GetName().GetString().Equals(RoomName, StringComparison.OrdinalIgnoreCase))
                    {
                        currentRoom = room;
                        roomManager.JoinRoom(currentRoom);
                        
                        Debug.LogFormat("InitializeRoom() - User Joining Room: {0}", room.GetName().GetString());
                        DebugDisplay(string.Format("\nInitializeRoom() - User Joining Room: {0}", room.GetName().GetString()));
                        break;
                    }
                }

                if (currentRoom == null)
                {                    
                    DebugDisplay(string.Format("\nCannot Find Matching Room: {0}\nWait for First Session User to Create", RoomName));
                }                
            }
            yield return new WaitForEndOfFrame();
        }

        var roomAnchorCount = currentRoom.GetAnchorCount();
        Debug.LogFormat("Room Anchor Count: {0}", roomAnchorCount);
        DebugDisplay(string.Format("\nRoom Anchor Count: {0}", roomAnchorCount));

        Debug.LogFormat("In room ({0}) with ID {1}",
            roomManager.GetCurrentRoom().GetName().GetString(),
            roomManager.GetCurrentRoom().GetID().ToString());
        DebugDisplay(string.Format("\nIn room ({0}) with ID {1}",
            roomManager.GetCurrentRoom().GetName().GetString(),
            roomManager.GetCurrentRoom().GetID().ToString()));

        if (roomAnchorCount == 0)
        {
#if UNITY_WSA && !UNITY_EDITOR            
            // If the room has no anchors, create the initial anchor            
            CurrentState = AnchorManagementState.CreateLocalAnchor;

#else                
            CurrentState = AnchorManagementState.GetRemoteAnchor;            
#endif
        }
        else
        {
            // Room already has anchors
            CurrentState = AnchorManagementState.GetRemoteAnchor;
        }
        
        yield return null;
    }

    private void ImportExportAnchorManager_OnTrackingChanged_Attaching(WorldAnchor self, bool located)
    {
        if (located)
        {
            // TODO: Inform Anchor is LOCKED            
            IsAnchorLocated = true;
        }
        else
        {
            IsAnchorLocated = false;
            Debug.LogWarning("Anchor Manager: Failed to find local anchor from cache.");            
            DebugDisplay(string.Format("\nFailed to find local anchor from cache."));
            MakeAnchorDataRequest();
        }
        self.OnTrackingChanged -= ImportExportAnchorManager_OnTrackingChanged_Attaching;
    }

    private bool AttachToCachedAnchor(string anchorName)
    {
        Debug.LogFormat("Anchor Manager: Looking for Remote Anchor: {0}", anchorName);
        DebugDisplay(string.Format("\nLooking for Remote Anchor: {0}", anchorName));

        string[] ids = anchorStore.GetAllIds();
        for (int index = 0; index < ids.Length; index++)
        {
            if (ids[index] == anchorName)
            {                
                Debug.LogFormat("Anchor Manager: Attempting to Load CACHED Anchor {0}", anchorName);
                DebugDisplay(string.Format("\nAttempting to Load CACHED anchor {0}", anchorName));

                WorldAnchor anchor = anchorStore.Load(ids[index], gameObject);
                if (anchor.isLocated)
                {
                    // TODO: Notify Anchor is Located                    
                    IsAnchorLocated = true;
                }
                else
                {
                    IsAnchorLocated = false;
                    DebugDisplay(string.Format("\nAnchor: {0} - Not Located", anchorName));
                    anchor.OnTrackingChanged += ImportExportAnchorManager_OnTrackingChanged_Attaching;                                        
                }
                return true;
            }
        }

        // Didn't find the anchor, so we'll download from room.
        return false;
    }

    private void GetRemoteAnchors()
    {
        IsAnchorConfigured = false;
        IsLocalAnchor = false;
        CurrentState = AnchorManagementState.GetRemoteAnchorStarting;

        // First, are there any anchors in this room?
        int anchorCount = currentRoom.GetAnchorCount();        
        Debug.LogFormat("Anchor Manager - Room Anchors Found: {0}", anchorCount.ToString());
        DebugDisplay(string.Format("\nRoom Anchors Found: {0}", anchorCount.ToString()));

#if UNITY_WSA && !UNITY_EDITOR
        // If there are anchors, we should attach to the last (most recent) one.
        if (anchorCount > 0)
        {
            // Extract the name of the anchor.
            string storedAnchorName;
            if (remoteAnchorName != string.Empty)
            {
                storedAnchorString = new XString(remoteAnchorName);                
            }
            else
            {
                storedAnchorString = currentRoom.GetAnchorName(anchorCount - 1);                
            }
            storedAnchorName = storedAnchorString.GetString();

            // Attempt to attach to the anchor in our local anchor store.
            if (AttachToCachedAnchor(storedAnchorName))
            {
                CurrentState = AnchorManagementState.CachedAnchorAttached;
                IsLocalAnchor = false;
                IsAnchorConfigured = true;                
            }
            else
            {
                Debug.Log("Anchor Manager: Starting room anchor download of " + storedAnchorString);
                DebugDisplay(string.Format("\nStarting room anchor download of " + storedAnchorString));

                // If we cannot find the anchor by name, we will need the full data blob.
                MakeAnchorDataRequest();
            }
        }
        else
        {
            // TODO: Failsafe Receovery Needed
            DebugDisplay("\nNo Remote Anchors Defined -- waiting for remote updates");
        }
#else
        DebugDisplay(anchorCount > 0 ? "\n" + currentRoom.GetAnchorName(0).ToString() : "\nNo Anchors Found");
        CurrentState = AnchorManagementState.Ready;
#endif
    }

    private void Anchor_OnTrackingChanged_InitialAnchor(WorldAnchor self, bool located)
    {
        if (located)
        {            
            Debug.Log("\nLocal Anchor Located - Ready for Export");
            DebugDisplay(string.Format("\nLocal Anchor Located - Ready for Export"));

            CurrentState = AnchorManagementState.ReadyToExportLocalAnchor;
            IsAnchorLocated = true;
        }
        else
        {
            Debug.LogError("Anchor Manager: Failed to locate local anchor!");
            DebugDisplay(string.Format("\nAnchor Manager: Failed to locate local anchor!"));

            IsAnchorLocated = false;            
        }
        self.OnTrackingChanged -= Anchor_OnTrackingChanged_InitialAnchor;
    }

    private void CreateLocalAnchor()
    {
        CurrentState = AnchorManagementState.CreatingLocalAnchor;

        // Use existing Anchor or Create as needed
        WorldAnchor anchor = gameObject.EnsureComponent<WorldAnchor>();

        IsLocalAnchor = true;
        IsAnchorConfigured = false;
        exportingAnchorBytes.Clear();
        
        if (anchor.isLocated)
        {
            CurrentState = AnchorManagementState.ReadyToExportLocalAnchor;
        }
        else
        {
            anchor.OnTrackingChanged += Anchor_OnTrackingChanged_InitialAnchor;
        }
    }

    private void WriteBuffer(byte[] data)
    {
        exportingAnchorBytes.AddRange(data);
    }

    private void ExportLocalAnchorComplete(SerializationCompletionReason status)
    {
        // TODO: ENFORCE Anchor Size LIMIT
        DebugDisplay(string.Format("\nExport Anchor Size {0} - {1}/{2}: {3}",
            exportingAnchorName, exportingAnchorBytes.Count, MinTrustworthySerializedAnchorDataSize,
            exportingAnchorBytes.Count > MinTrustworthySerializedAnchorDataSize));

        if (status == SerializationCompletionReason.Succeeded && exportingAnchorBytes.Count > MinTrustworthySerializedAnchorDataSize)
        {
            Debug.LogFormat("Uploading anchor: {0}", exportingAnchorName);
            DebugDisplay(string.Format("\nUploading anchor: {0}", exportingAnchorName));

            roomManager.UploadAnchor(
                currentRoom,
                new XString(exportingAnchorName),
                exportingAnchorBytes.ToArray(),
                exportingAnchorBytes.Count);            
        }
        else
        {
            Debug.LogWarning("Anchor Manager: Failed to upload anchor, trying again...");            
            DebugDisplay(string.Format("\nFailed to upload anchor, trying again..."));

            CurrentState = AnchorManagementState.CreateLocalAnchor;
        }
    }

    private void ExportLocalAnchor()
    {
        CurrentState = AnchorManagementState.ExportingLocalAnchor;

        WorldAnchor anchor = gameObject.GetComponent<WorldAnchor>();
        string guidString = Guid.NewGuid().ToString();        
        exportingAnchorName = guidString;

        // Save the anchor to our local anchor store.
        if (anchor != null && anchorStore.Save(exportingAnchorName, anchor))
        {
            Debug.LogFormat("Exporting anchor: {0}", exportingAnchorName);
            DebugDisplay(string.Format("\nExporting anchor: {0}", exportingAnchorName));

            sharedAnchorInterface = new WorldAnchorTransferBatch();
            sharedAnchorInterface.AddWorldAnchor(guidString, anchor);
            WorldAnchorTransferBatch.ExportAsync(sharedAnchorInterface, WriteBuffer, ExportLocalAnchorComplete);
        }
        else
        {
            Debug.LogWarning("Anchor Manager: Failed to export anchor, trying again...");            
            DebugDisplay(string.Format("\nFailed to export anchor, trying again..."));

            CurrentState = AnchorManagementState.LocalAnchorExportFailed;
        }
    }

    private void AnchorImportComplete(SerializationCompletionReason status, WorldAnchorTransferBatch anchorBatch)
    {
        if (status == SerializationCompletionReason.Succeeded)
        {
            if (anchorBatch.GetAllIds().Length > 0)
            {
                // HACK: Specify Anchor ID
                //string anchorEntry = anchorBatch.GetAllIds();
                string anchorEntry = storedAnchorString.GetString();

                Debug.LogFormat("Anchor Manager: Sucessfully Attached Remote Anchor: {0}", anchorEntry);
                DebugDisplay(string.Format("\nSucessfully Attached Remote Anchor: {0}", anchorEntry));

                WorldAnchor anchor = anchorBatch.LockObject(anchorEntry, gameObject);
                anchorStore.Save(anchorEntry, anchor);
                remoteAnchorName = string.Empty;
            }

            IsAnchorConfigured = true;
            IsLocalAnchor = false;
            CurrentState = AnchorManagementState.RemoteAnchorAttached;
        }
        else
        {
            IsAnchorConfigured = false;
            Debug.LogError("Remote Anchor Attach Failed");
            DebugDisplay("\nRemote Anchor Attach Failed");
            
            CurrentState = AnchorManagementState.RemoteAnchorDataReady;
        }
    }

    public bool IsPlacementAllowed()
    {
        if ((_currentState == AppShareControl.AnchorManagementState.Ready) ||
            (_currentState == AppShareControl.AnchorManagementState.AnchorStoreReady))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private void ClearPlacementObjectAnchors()
    {
        foreach (var anchor in gameObject.GetComponents<WorldAnchor>())
        {
            DebugDisplay(string.Format("\nDeleting GameObject Anchor: {0} - {1}", anchor.name, anchor.gameObject.name));
            DestroyImmediate(anchor);
            anchorStore.Clear();            
        }
    }

    public void PlacementStart()
    {
        _previousTrackingState = _currentState;
        DebugDisplay(string.Format("Previouse State: {0}", _previousTrackingState.ToString()));
        CurrentState = AnchorManagementState.AnchorPlacementStart;
    }

    public void PlacementDone()
    {
        CurrentState = AnchorManagementState.AnchorPlacementDone;
    }

    void Update()
    {
        switch (_currentState)
        {
            case AnchorManagementState.WaitingForAnchorStore:
                // Initial State = Event Callback from AnchorStore.GetAsync()
                break;
            case AnchorManagementState.AnchorStoreReady:
                // Setup Sharing Session and Room when the AnchorStore is ready for use
                if (IsSharingManagerConnected && IsSharingSessionConnected)                
                    CurrentState = AnchorManagementState.InitializeRoom;
                break;
            case AnchorManagementState.InitializeRoom:                
                StartCoroutine(InitializeRoom());                
                break;
            case AnchorManagementState.InitializingRoom:
                break;
            case AnchorManagementState.GetRemoteAnchor:                
                GetRemoteAnchors();                
                break;
            case AnchorManagementState.CreateLocalAnchor:
                CreateLocalAnchor();
                break;
            case AnchorManagementState.ReadyToExportLocalAnchor:
                ExportLocalAnchor();
                break;
            case AnchorManagementState.LocalAnchorExported:
                CurrentState = AnchorManagementState.Ready;
                break;
            case AnchorManagementState.LocalAnchorExportFailed:
                break;
            case AnchorManagementState.RemoteAnchorDataReady:
                CurrentState = AnchorManagementState.RemoteAnchorAttaching;
                WorldAnchorTransferBatch.ImportAsync(anchorDownloadRawBytes, AnchorImportComplete);
                break;
            case AnchorManagementState.RemoteAnchorAttaching:
                break;
            case AnchorManagementState.RemoteAnchorAttached:
                CurrentState = AnchorManagementState.Ready;
                break;
            case AnchorManagementState.RemoteAnchorAttachFailed:
                break;
            case AnchorManagementState.CachedAnchorAttached:
                CurrentState = AnchorManagementState.Ready;
                break;

            case AnchorManagementState.Ready:
                break;
            case AnchorManagementState.AnchorPlacementStart:                
                CurrentState = AnchorManagementState.AnchorPlacement;
                ClearPlacementObjectAnchors();
                break;
            case AnchorManagementState.AnchorPlacement:
                break;
            case AnchorManagementState.AnchorPlacementDone:                
                if (_previousTrackingState == AnchorManagementState.Ready)
                    CurrentState = AnchorManagementState.CreateLocalAnchor;
                else
                    CurrentState = _previousTrackingState;
                break;
        }
    }
}
