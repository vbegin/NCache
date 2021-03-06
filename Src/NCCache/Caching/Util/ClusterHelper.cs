// Copyright (c) 2015 Alachisoft
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//    http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Collections;

using Alachisoft.NGroups;
using Alachisoft.NGroups.Util;
using Alachisoft.NCache.Caching.Statistics;
using Alachisoft.NCache.Runtime.Exceptions;
using Alachisoft.NCache.Caching;
using Alachisoft.NCache.Caching.Topologies;
using Alachisoft.NCache.Common.Net;
using Alachisoft.NCache.Serialization.Formatters;
using Alachisoft.NCache.Common;
using Alachisoft.NCache.Common.Logger;
using Alachisoft.NCache.Common.DataStructures;
using Runtime = Alachisoft.NCache.Runtime;

namespace Alachisoft.NCache.Caching.Util
{
    /// <summary>
    /// Deals in tasks specific to Cluster cache implementations.
    /// </summary>
    internal class ClusterHelper
    {
        public static void ValidateResponses(RspList results, Type type, string serializationContext)
        {
            if (results == null) return;

            //muds:

            ArrayList parserExceptions = new ArrayList();
            ArrayList exceptions = new ArrayList(11);
            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    continue;
                }

                if (!rsp.wasReceived())
                {
                    continue;
                }

                if (rsp.Value != null)
                {
                    object rspValue = rsp.Value;

                    if (rspValue is ParserException)
                    {
                        parserExceptions.Add((Exception)rspValue);
                        continue;
                    }

                    if (rspValue is Exception)
                    {
                        exceptions.Add((Exception)rspValue);
                        continue;
                    }

                    if (type != null && !rspValue.GetType().Equals(type))
                    {
                        exceptions.Add(new BadResponseException("bad response returned by group member " + rsp.Sender));
                        continue;
                    }
                }
            }
            //muds:
            //in case or partitioned caches search requests are broadcasted.
            //it is possible that tag index are defined on one node but not defined on some other node.
            //we will throw the exception back only if we receive exception from every node.
            if (parserExceptions.Count == results.size())
            {
                Exception e = parserExceptions[0] as Exception;
                throw e;
            }

            if (exceptions.Count == 1)
            {
                Exception e = exceptions[0] as Exception;
                if (e is CacheException) 
                    throw e;
                else
                    throw new RemoteException((Exception)exceptions[0]);
            }
            else if (exceptions.Count > 0)
            {
                for (int i = 0; i < exceptions.Count; i++)
                {
                    Exception e = exceptions[i] as Exception;
                    if (e is LockingException) 
                        throw e;
                    else if (e is CacheException)
                        continue;
                    else
                        exceptions[i] = new RemoteException(e);
                }
                throw new Runtime.Exceptions.AggregateException(exceptions);
            }
        }

        /// <summary>
        /// Returns the set of nodes where the addition was performed as an atomic operation.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static CacheAddResult FindAtomicAddStatusReplicated(RspList results)
        {
            CacheAddResult res = CacheAddResult.Failure;
            if (results == null) return res;
            int timeoutCount = 0;
            int suspectedCount = 0;
            int successCount = 0;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    //throw new Alachisoft.NGroups.SuspectedException(rsp.Sender);
                    suspectedCount++;
                    continue;
                }
                if (!rsp.wasReceived() && !rsp.wasSuspected())
                {
                    //throw new Alachisoft.NGroups.TimeoutException();
                    timeoutCount++;
                    continue;
                }
                res = (CacheAddResult)rsp.Value;
                if (res == CacheAddResult.Success)
                {
                    successCount++;
                }
                if (res != CacheAddResult.Success && res != CacheAddResult.KeyExists)
                {
                    return res;
                }
            }
            if (suspectedCount > 0 && successCount > 0 && (suspectedCount + successCount == results.size()))
            {
                //as operation is successfull on all other nodes other than suspected node(s).
                return CacheAddResult.Success;
            }
            if (timeoutCount > 0 && (timeoutCount + successCount == results.size()))
            {
                if (successCount > 0)
                {
                    //operation is not succeeded on some of the nodes; therefore we throw timeout exception.
                    return CacheAddResult.PartialTimeout;
                }
                else
                {
                    //operation timed out on all of the node; no need to rollback.
                    return CacheAddResult.FullTimeout;
                }
            }
            if (timeoutCount > 0 && suspectedCount > 0)
            {
                if (successCount > 0)
                {
                    return CacheAddResult.PartialTimeout;
                }
                else
                {
                    return CacheAddResult.FullTimeout;
                }
            }

            return res;
        }

        public static Rsp FindAtomicRemoveStatusReplicated(RspList results)
        {
            return FindAtomicRemoveStatusReplicated(results, null);
        }
        /// <summary>
        /// Returns the set of nodes where the addition was performed as an atomic operation.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static Rsp FindAtomicRemoveStatusReplicated(RspList results, ILogger NCacheLog)
        {
            Rsp retRsp = null;
            if (results == null) return retRsp;
            int timeoutCount = 0;
            int suspectedCount = 0;
            int successCount = 0;
            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);


                if (!rsp.wasReceived() && !rsp.wasSuspected())
                {
                    
                    timeoutCount++;
                    continue;
                }
                if (rsp.wasSuspected())
                {
                    
                    suspectedCount++;
                    continue;
                }
                if (rsp.Value != null)
                {
                    retRsp = rsp;
                }
                successCount++;

            }
            if (suspectedCount > 0 && successCount > 0 && (suspectedCount + successCount == results.size()))
            {
                //as operation is successfull on all other nodes other than suspected node(s).
                return retRsp;
            }
            if (timeoutCount > 0 && (timeoutCount + successCount == results.size()))
            {
                throw new Runtime.Exceptions.TimeoutException("Operation Timeout");
            }
            if (timeoutCount > 0 && suspectedCount > 0)
            {
                throw new Runtime.Exceptions.TimeoutException("Operation Timeout");

            }

            return retRsp;
        }


        public static LockOptions FindAtomicIsLockedStatusReplicated(RspList results, ref object lockId, ref DateTime lockDate)
        {
            LockOptions lockInfo = null;
            if (results == null) return lockInfo;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    continue;
                }

                lockInfo = (LockOptions)rsp.Value;
                if (lockInfo != null)
                {
                    if (lockInfo.LockId != null) return lockInfo;
                }
            }
            return lockInfo;
        }


        public static bool FindAtomicLockStatusReplicated(RspList results, ref object lockId, ref DateTime lockDate)
        {
            bool res = true;
            LockOptions lockInfo = null;
            if (results == null) return res;
            int lockAcquired = 0;
            int itemNotFound = 0;
            int rspReceived = results.size();

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    rspReceived--;
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    rspReceived--;
                    continue;
                }

                lockInfo = (LockOptions)rsp.Value;
                if (Object.Equals(lockInfo.LockId, lockId))
                {
                    lockDate = lockInfo.LockDate;
                    lockAcquired++;
                }
                else
                {
                    if (lockInfo.LockId == null)
                    {
                        //item was not present on the node.
                        lockId = null;
                        lockDate = new DateTime();
                        itemNotFound++;
                    }
                    else
                    {
                        res = false;
                        lockId = lockInfo.LockId;
                        lockDate = lockInfo.LockDate;
                        break;
                    }
                }

            }
            if (lockAcquired > 0 && (lockAcquired + itemNotFound == rspReceived))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the next enumeration data chunk from the set of chunks returned by multiple nodes
        /// </summary>
        /// <param name="results"></param>
        /// <returns></returns>
        public static EnumerationDataChunk FindAtomicEnumerationDataChunkReplicated(RspList results)
        {
            EnumerationDataChunk nextChunk = null;
            if (results == null) return nextChunk;


            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (!rsp.wasReceived() || rsp.wasSuspected())
                    continue;

                nextChunk = (EnumerationDataChunk)rsp.Value;

                if (nextChunk != null)
                {
                    return nextChunk;
                }
            }
            return nextChunk;
        }

        /// <summary>
        /// Returns the set of nodes where the addition was performed as an atomic operation.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static bool FindAtomicAddHintReplicated(RspList results)
        {
            bool res = false;
            if (results == null) return res;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (!rsp.wasReceived() || rsp.wasSuspected())
                    continue;

                res = (bool)rsp.Value;
                if (res == false)
                {
                    return res;
                }
            }
            return res;
        }



        /// <summary>
        /// Returns the array of keys for which Bulk operation failed.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static object[] FindAtomicBulkOpStatusReplicated(RspList results, Address local)
        {
            Hashtable failedKeys = new Hashtable();

            object[] result = null;
            if (results == null) return null;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    
                    continue;
                }

                result = (object[])rsp.Value;

                for (int j = 0; j < result.Length; j++)
                {
                    if (failedKeys.Contains(result[j]) == false)
                    {
                        failedKeys[result[j]] = result[j];
                    }
                }

                /*
                if(Trace.isInfoEnabled) Trace.info("FindAtomicAddStatusReplicated", "Sender = " + rsp.Sender + ", result = " + res);
                if(res != CacheAddResult.Success && res != CacheAddResult.KeyExists) 
                {
                    return res;
                }
                */
            }

            object[] failed = new object[failedKeys.Count];
            failedKeys.Keys.CopyTo(failed, 0);

            return failed;
        }


        /// <summary>
        /// Returns the set of nodes where the insertion was performed as an atomic operation.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static CacheInsResultWithEntry FindAtomicInsertStatusReplicated(RspList results)
        {
            int needEvictCount = 0;
            int timeoutCount = 0;
            int suspectedCount = 0;
            int successCount = 0;

            CacheInsResultWithEntry res = new CacheInsResultWithEntry();
            if (results == null)
                return res;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);
                if (!rsp.wasReceived() && !rsp.wasSuspected())
                {
                    
                    timeoutCount++;
                    continue;
                }

                if (rsp.wasSuspected())
                {
                    
                    suspectedCount++;
                    continue;
                }


                res = (CacheInsResultWithEntry)((OperationResponse)rsp.Value).SerializablePayload;
                if (res.Result == CacheInsResult.Success || res.Result == CacheInsResult.SuccessOverwrite)
                {
                    successCount++;
                }
                if (res.Result != CacheInsResult.Success && res.Result != CacheInsResult.SuccessOverwrite && res.Result != CacheInsResult.NeedsEviction)
                {
                }

                /* If all the nodes in the Cluster return NeedsEviction response then we do not need to remove */
                if (res.Result == CacheInsResult.NeedsEviction)
                {
                    needEvictCount++;
                }
            }

            if (needEvictCount == results.size())
            {
                //every node returned the NeedEviction; so we need not remove the item
                //as data is not corrupted.
                res.Result = CacheInsResult.NeedsEvictionNotRemove;
            }
            if (suspectedCount > 0 && successCount > 0 && (suspectedCount + successCount == results.size()))
            {
                //as operation is successfull on all other nodes other than suspected node(s).
            }
            if (timeoutCount > 0 && (timeoutCount + successCount == results.size()))
            {
                if (successCount > 0)
                {
                    //operation is not succeeded on some of the nodes; therefore we throw timeout exception.
                    res.Result = CacheInsResult.PartialTimeout;
                }
                else
                {
                    //operation timed out on all of the node; no need to rollback.
                    res.Result = CacheInsResult.FullTimeout;
                }
            }
            if (timeoutCount > 0 && suspectedCount > 0)
            {
                if (successCount > 0)
                {
                    //operation is not succeeded on some of the nodes; therefore we throw timeout exception.
                    res.Result = CacheInsResult.PartialTimeout;
                }
                else
                {
                    //operation timed out on all of the node; no need to rollback.
                    res.Result = CacheInsResult.FullTimeout;
                }
            }

            return res;
        }


        /// <summary>
        /// Returns the set of nodes where the insertion was performed as an atomic operation.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>key and value pairs for inserted items</returns>
        public static Hashtable FindAtomicBulkInsertStatusReplicated(RspList results)
        {
            Hashtable insertedKeys = new Hashtable();
            Hashtable result = null;
            Hashtable prvResult = null;

            if (results == null) return insertedKeys;

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    
                    continue;
                }

                result = (Hashtable)rsp.Value;

                if (prvResult == null)
                {
                    insertedKeys = result;
                    prvResult = result;
                }
                else
                {
                    foreach (object key in prvResult.Keys)
                    {
                        if (result.Contains(key) == false)
                        {
                            if (insertedKeys.Contains(key))
                                insertedKeys.Remove(key);
                        }
                    }

                    prvResult = result;
                }
            }
            return insertedKeys;
        }


        /// <summary>
        /// Find first entry in the response list that is not null and didnt timeout.
        /// </summary>
        /// <param name="results">response list</param>
        /// <returns>found entry</returns>
        public static Rsp GetFirstNonNullRsp(RspList results)
        {
            if (results == null)
                return null;

            Rsp rsp = null;
            for (int i = 0; i < results.size(); i++)
            {
                rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    
                    results.removeElementAt(i);
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    
                    results.removeElementAt(i);
                    continue;
                }

                if (rsp.Value != null) return rsp;
            }
            return null;
        }




        /// <summary>
        /// Find first entry in the response list that is not null and didnt timeout.
        /// </summary>
        /// <param name="results">response list</param>
        /// <param name="type">type of response to fetch</param>
        /// <returns>found entry</returns>
        public static Rsp GetFirstNonNullRsp(RspList results, Type type)
        {
            if (results == null)
                return null;

            Rsp rsp = null;
            for (int i = 0; i < results.size(); i++)
            {
                rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    
                    results.removeElementAt(i);
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    
                    results.removeElementAt(i);
                    continue;
                }

                if (rsp.Value != null && rsp.Value.GetType().Equals(type)) return rsp;
            }
            return null;
        }


        /// <summary>
        /// Find all entries in the response list that are not null and didnt timeout.
        /// </summary>
        /// <param name="results">response list</param>
        /// <param name="type">type of response to fetch</param>
        /// <returns>List of entries found</returns>
        public static ArrayList GetAllNonNullRsp(RspList results, Type type)
        {
            ArrayList list = new ArrayList();
            if (results == null)
                return null;

            Rsp rsp = null;
            for (int i = 0; i < results.size(); i++)
            {
                rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    results.removeElementAt(i);
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    results.removeElementAt(i);
                    continue;
                }

                if (rsp.Value != null && rsp.Value.GetType().Equals(type))
                {
                    list.Add(rsp);
                }
            }
            return list;
        }

        /// <summary>
        /// Returns the array of keys for which Bulk operation failed.
        /// </summary>
        /// <param name="results">responses collected from all members of cluster.</param>
        /// <returns>list of nodes where the operation succeeded</returns>
        public static Hashtable FindAtomicBulkRemoveStatusReplicated(RspList results, Address local)
        {
            Hashtable result = null;

            if (results == null) return new Hashtable();

            for (int i = 0; i < results.size(); i++)
            {
                Rsp rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    
                    continue;
                }
                if (!rsp.wasReceived())
                {
                    
                    continue;
                }

                result = (Hashtable)rsp.Value;

                if (result != null)
                {
                    return result;
                }

            }

            return result;
        }


        /// <summary>
        /// Combines the collected statistics of the nodes, in a partitioned environment.
        /// </summary>
        /// <returns></returns>
        public static CacheStatistics CombinePartitionReplicasStatistics(ClusterCacheStatistics s)
        {
            CacheStatistics stats = new CacheStatistics();
            if (s.Nodes == null) return stats;

            bool zeroSeen = false;
            for (int i = 0; i < s.Nodes.Count; i++)
            {
                NodeInfo info = s.Nodes[i] as NodeInfo;
                if (info == null || info.Statistics == null ||
                    !info.Status.IsAnyBitSet(NodeStatus.Coordinator | NodeStatus.SubCoordinator)) continue;

                stats.HitCount += info.Statistics.HitCount;
                stats.MissCount += info.Statistics.MissCount;
                stats.UpdateCount(stats.Count + info.Statistics.Count);
                stats.MaxCount += info.Statistics.MaxCount;
                if (info.Statistics.MaxCount == 0)
                    zeroSeen = true;
            }

            stats.MaxSize = s.LocalNode.Statistics.MaxSize;

            if (zeroSeen)
                stats.MaxCount = 0;
            return stats;
        }

        /// <summary>
        /// Combines the collected statistics of the nodes, in a partitioned environment.
        /// </summary>
        /// <returns></returns>
        public static CacheStatistics CombinePartitionStatistics(ClusterCacheStatistics s)
        {
            CacheStatistics stats = new CacheStatistics();
            if (s.Nodes == null) return stats;

            bool zeroSeen = false;
            for (int i = 0; i < s.Nodes.Count; i++)
            {
                NodeInfo info = s.Nodes[i] as NodeInfo;
                if (info == null || info.Statistics == null) continue;

                stats.HitCount += info.Statistics.HitCount;
                stats.MissCount += info.Statistics.MissCount;
                stats.UpdateCount(stats.Count + info.Statistics.Count);
                stats.MaxCount += info.Statistics.MaxCount;
                if (info.Statistics.MaxCount == 0)
                    zeroSeen = true;
            }

            stats.MaxSize = s.LocalNode.Statistics.MaxSize;

            if (zeroSeen)
                stats.MaxCount = 0;
            return stats;
        }

        /// <summary>
        /// Combines the collected statistics of the nodes, in a replicated environment.
        /// </summary>
        /// <returns></returns>
        public static CacheStatistics CombineReplicatedStatistics(ClusterCacheStatistics s)
        {
            CacheStatistics stats = new CacheStatistics();
            if (s.Nodes == null) return stats;

            for (int i = 0; i < s.Nodes.Count; i++)
            {
                NodeInfo info = s.Nodes[i] as NodeInfo;
                if (info == null || info.Statistics == null) continue;

                stats.HitCount += info.Statistics.HitCount;
                stats.MissCount += info.Statistics.MissCount;
            }

            stats.UpdateCount(s.LocalNode.Statistics.Count);
            stats.MaxCount = s.LocalNode.Statistics.MaxCount;
            stats.MaxSize = s.LocalNode.Statistics.MaxSize;
            stats.SessionCount = s.LocalNode.Statistics.SessionCount;
            return stats;
        }

        public static Hashtable VerifyAllTrueResopnses(RspList results)
        {
            Hashtable res = new Hashtable();
            if (results == null)
                return res;
            Rsp rsp = null;
            for (int i = 0; i < results.size(); i++)
            {
                rsp = (Rsp)results.elementAt(i);

                if (rsp.wasSuspected())
                {
                    throw new Alachisoft.NGroups.SuspectedException(rsp.Sender);
                }

                if (!rsp.wasReceived())
                {
                    throw new Runtime.Exceptions.TimeoutException("operation timeout");
                }
            }
            if (rsp != null)
                return (Hashtable)rsp.Value;
            else
                return null;
        }
    }
}

