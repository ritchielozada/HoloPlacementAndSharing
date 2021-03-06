﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using HoloToolkit.Unity.InputModule;
using UnityEngine;
using UnityEngine.VR.WSA;

namespace HoloToolkit.Unity.SpatialMapping
{
    /// <summary>
    /// The TapToPlace class is a basic way to enable users to move objects 
    /// and place them on real world surfaces.
    /// Put this script on the object you want to be able to move. 
    /// Users will be able to tap objects, gaze elsewhere, and perform the
    /// tap gesture again to place.
    /// This script is used in conjunction with GazeManager, GestureManager,
    /// and SpatialMappingManager.
    /// TapToPlace also adds a WorldAnchor component to enable persistence.
    /// </summary>

    public class TapToPlace2 : MonoBehaviour, IInputClickHandler
    {
        [SerializeField] private TextMesh DebugText;

        //[Tooltip("Place parent on tap instead of current game object.")]
        //public bool PlaceParentOnTap;

        //[Tooltip("Specify the parent game object to be moved on tap, if the immediate parent is not desired.")]
        public GameObject ParentGameObjectToPlace;

        /// <summary>
        /// Keeps track of if the user is moving the object or not.
        /// Setting this to true will enable the user to move and place the object in the scene.
        /// Useful when you want to place an object immediately.
        /// </summary>
        [Tooltip("Setting this to true will enable the user to move and place the object in the scene without needing to tap on the object. Useful when you want to place an object immediately.")]
        public bool IsBeingPlaced;

        /// <summary>
        /// Controls spatial mapping.  In this script we access spatialMappingManager
        /// to control rendering and to access the physics layer mask.
        /// </summary>
        protected SpatialMappingManager spatialMappingManager;

        protected AppShareControl appShareControl;

        private Vector3 localPositionOffset;

        private void DebugDisplay(string msg)
        {
            if (DebugText != null)
            {
                DebugText.text += msg;
            }
        }

        protected virtual void Start()
        {
            localPositionOffset = gameObject.transform.localPosition;  // Save Offset for reconnection

            appShareControl = AppShareControl.Instance;
            if (appShareControl == null)
            {
                Debug.LogError("TapToPlace2: Requires AppShareControl");
            }

            spatialMappingManager = SpatialMappingManager.Instance;
            if (spatialMappingManager == null)
            {
                Debug.LogError("This script expects that you have a SpatialMappingManager component in your scene.");
            }            

            //if (PlaceParentOnTap)
            //{
            //    if (ParentGameObjectToPlace != null && !gameObject.transform.IsChildOf(ParentGameObjectToPlace.transform))
            //    {
            //        Debug.LogError("The specified parent object is not a parent of this object.");
            //    }

            //    DetermineParent();
            //}
        }

        protected virtual void Update()
        {
            // If the user is in placing mode,
            // update the placement to match the user's gaze.
            if (IsBeingPlaced)
            {
                // Do a raycast into the world that will only hit the Spatial Mapping mesh.
                Vector3 headPosition = Camera.main.transform.position;
                Vector3 gazeDirection = Camera.main.transform.forward;

                RaycastHit hitInfo;
                if (Physics.Raycast(headPosition, gazeDirection, out hitInfo, 30.0f, spatialMappingManager.LayerMask))
                {
                    // Rotate this object to face the user.
                    Quaternion toQuat = Camera.main.transform.localRotation;
                    toQuat.x = 0;
                    toQuat.z = 0;

                    gameObject.transform.position = hitInfo.point;
                    gameObject.transform.rotation = toQuat;

                    // Move this object to where the raycast
                    // hit the Spatial Mapping mesh.
                    // Here is where you might consider adding intelligence
                    // to how the object is placed.  For example, consider
                    // placing based on the bottom of the object's
                    // collider so it sits properly on surfaces.
                    //if (PlaceParentOnTap)
                    //{
                    //    // Place the parent object as well but keep the focus on the current game object
                    //    Vector3 currentMovement = hitInfo.point - gameObject.transform.position;
                    //    ParentGameObjectToPlace.transform.position += currentMovement;
                    //    ParentGameObjectToPlace.transform.rotation = toQuat;
                    //}
                    //else
                    //{
                    //    gameObject.transform.position = hitInfo.point;
                    //    gameObject.transform.rotation = toQuat;
                    //}
                }
            }
        }

        public virtual void OnInputClicked(InputClickedEventData eventData)
        {
            // On each tap gesture, toggle whether the user is in placing mode.
            IsBeingPlaced = !IsBeingPlaced;

            // If the user is in placing mode, display the spatial mapping mesh.
            if (IsBeingPlaced)
            {
                if (appShareControl.IsPlacementAllowed())
                {
                    appShareControl.PlacementStart();                    
                    spatialMappingManager.DrawVisualMeshes = true;
                    gameObject.transform.parent = null;
                    DebugDisplay("\nTap Placement Active");
                }
                else
                {
                    // Cancel Placement if Sharing System Not Ready
                    IsBeingPlaced = false;
                    DebugDisplay("\nTap Placement Not Allowed");
                }
            }            
            else
            {
                spatialMappingManager.DrawVisualMeshes = false;
                ParentGameObjectToPlace.transform.position = gameObject.transform.position - localPositionOffset;
                ParentGameObjectToPlace.transform.rotation = gameObject.transform.rotation;
                gameObject.transform.parent = ParentGameObjectToPlace.transform;

                appShareControl.PlacementDone();                                
                DebugDisplay("\nTap Placement DONE!");                
            }
        }

        private void DetermineParent()
        {
            if (ParentGameObjectToPlace == null)
            {
                if (gameObject.transform.parent == null)
                {
                    Debug.LogError("The selected GameObject has no parent.");
                    //PlaceParentOnTap = false;
                }
                else
                {
                    Debug.LogError("No parent specified. Using immediate parent instead: " + gameObject.transform.parent.gameObject.name);
                    ParentGameObjectToPlace = gameObject.transform.parent.gameObject;
                }
            }
        }
    }
}
