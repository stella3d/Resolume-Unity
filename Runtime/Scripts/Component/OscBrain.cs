﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using OscJack;
using UnityEngine;

namespace Resolunity
{
    internal sealed class TemplateChecker
    {
        public Regex[] Regexes;
        public Action<OscDataHandle>[] Handlers;

        public int Count { get; private set; }

        public TemplateChecker(int capacity = 8)
        {
            Regexes = new Regex[capacity];
            Handlers = new Action<OscDataHandle>[capacity];
        }

        public void AddRegexHandler(Regex regex, Action<OscDataHandle> handler)
        {
            if (Count >= Handlers.Length)
            {
                Array.Resize(ref Regexes, Regexes.Length * 2);
                Array.Resize(ref Handlers, Handlers.Length * 2);
            }

            Regexes[Count] = regex;
            Handlers[Count] = handler;
            Count++;
        }

        public bool Process(string address, out Action<OscDataHandle> handler)
        {
            for (var i = 0; i < Regexes.Length; i++)
            {
                var regex = Regexes[i];
                if (regex.IsMatch(address))
                {
                    handler = Handlers[i];
#if RESOLUNITY_DEBUG_ADDRESS_MATCHING || true
                    Debug.Log($"{regex} matched {address}");
#endif
                    return true;
                }
            }

            handler = null;
            return false;
        }
    }

    [ExecuteAlways]
    public class OscBrain : MonoBehaviour
    {
        readonly HashSet<OscServer> m_KnownServers = new HashSet<OscServer>();
        
        internal readonly Dictionary<string, List<Action<OscDataHandle>>> m_AddressHandlers = 
            new Dictionary<string, List<Action<OscDataHandle>>>(32);
        
        internal readonly Dictionary<string, Action<OscDataHandle>> m_WildcardAddressHandlers = 
            new Dictionary<string, Action<OscDataHandle>>(8);

        /// <summary>
        /// Every incoming osc address we tried to find a template handler for and failed
        /// </summary>
        readonly HashSet<string> m_AddressesToIgnore = new HashSet<string>();
        
        readonly ActionInvocationBuffer<OscDataHandle> m_ActionInvocationBuffer = 
            new ActionInvocationBuffer<OscDataHandle>();

        bool m_PrimaryCallbackAdded;
        int m_PreviousServerCount;

        TemplateChecker m_TemplateChecker = new TemplateChecker(8);
        
        public static OscBrain Instance { get; protected set; }

        void OnEnable()
        {
            Instance = this;
            AddPrimaryCallback(PrimaryCallback);
            m_PrimaryCallbackAdded = true;
        }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (!m_PrimaryCallbackAdded)
            {
                AddPrimaryCallback(PrimaryCallback);
                m_PrimaryCallbackAdded = true;
            }
        }

        void OnDisable()
        {
            m_PrimaryCallbackAdded = false;
            RemovePrimaryCallback(PrimaryCallback);
        }

        void Update()
        {
            // call all Actions buffered in response to messages since last frame
            m_ActionInvocationBuffer.InvokeAll();

            // handle the existence of any next osc servers
            if (OscServer.ServerList.Count == m_PreviousServerCount) 
                return;
            
            foreach (var server in OscServer.ServerList)
            {
                if (m_KnownServers.Contains(server))
                    continue;

                server.MessageDispatcher.AddCallback(string.Empty, PrimaryCallback);
                m_KnownServers.Add(server);
            }

            m_PreviousServerCount = OscServer.ServerList.Count;
        }
        
        /// <summary>
        /// Register a new OSC message handler  
        /// </summary>
        /// <param name="address">The URL path to handle messages for</param>
        /// <param name="callback">The action to take when the message is received</param>
        public static void AddCallback(string address, Action<OscDataHandle> callback)
        {
            if (PathUtils.IsWildcardTemplate(address))
            {
                if (Instance.m_WildcardAddressHandlers.ContainsKey(address))
                {
                    Debug.LogWarning($"A wildcard handler for {address} has already been registered");
                    return;
                }

                Instance.m_WildcardAddressHandlers[address] = callback;
            }

            if (!Instance.m_AddressHandlers.TryGetValue(address, out var callbackList))
            {
                callbackList = new List<Action<OscDataHandle>>();
                Instance.m_AddressHandlers[address] = callbackList;
            }
            
            callbackList.Add(callback);
        }
        
        void AddCallbackInternal(string address, Action<OscDataHandle> callback)
        {
            if (!m_AddressHandlers.TryGetValue(address, out var callbackList))
            {
                callbackList = new List<Action<OscDataHandle>>();
                m_AddressHandlers[address] = callbackList;
            }
            
            callbackList.Add(callback);
        }
        
        /// <summary>
        /// Remove a previously registered OSC message handler  
        /// </summary>
        /// <param name="address">The URL path to handle messages for</param>
        /// <param name="callback">The action to remove</param>
        public static void RemoveCallback(string address, Action<OscDataHandle> callback)
        {
            if (Instance.m_AddressHandlers.TryGetValue(address, out var callbackList))
                callbackList.Remove(callback);
        }

        public static void AddPrimaryCallback(OscMessageDispatcher.MessageCallback callback)
        {
            foreach (var server in OscServer.ServerList)
                server.MessageDispatcher.AddCallback(string.Empty, callback);
        }

        public static void RemovePrimaryCallback(OscMessageDispatcher.MessageCallback callback)
        {
            foreach (var server in OscServer.ServerList)
                server.MessageDispatcher.RemoveCallback(string.Empty, callback);
        }

        /// <summary>
        /// This is the message handler for every OSC message we receive
        /// </summary>
        /// <param name="address">The URL path where this message was received</param>
        /// <param name="handle">A handle to access the value of the message</param>
        protected void PrimaryCallback(string address, OscDataHandle handle)
        {
#if DEBUG_OSC_CALLBACK
            Debug.Log(address + " " + handle.GetElementAsString(0));
#endif
            if (!m_AddressHandlers.TryGetValue(address, out var callbackList))
            {
                if (m_AddressesToIgnore.Contains(address))
                    return;

                // if we find a match in the template handlers, add a handler, otherwise ignore this address
                if (m_TemplateChecker.Process(address, out var handler))
                    AddCallbackInternal(address, handler);
                else
                    m_AddressesToIgnore.Add(address);

                return;
            }

            // This callback will be called on another thread, but UnityEvents can only be called on the main thread.
            // So we buffer all the actions here and call them at the start of next frame
            foreach (var callback in callbackList)
            {
                m_ActionInvocationBuffer.Add(callback, handle);
            }
        }

        /// <summary>
        /// Messages arriving at addresses we can't find handlers for get added to an ignore set.
        /// Call this to clear that - You might do this if handlers were added later.
        /// This should not be needed in most cases
        /// </summary>
        public static void ClearIgnoredAddresses()
        {
            Instance.m_AddressesToIgnore.Clear();
        }
    }
}