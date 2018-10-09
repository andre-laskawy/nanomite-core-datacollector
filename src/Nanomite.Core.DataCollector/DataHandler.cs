///-----------------------------------------------------------------
///   File:         DataHandler.cs
///   Author:   	Andre Laskawy           
///   Date:         09.10.2018 18:11:24
///-----------------------------------------------------------------

namespace Nanomite.Core.DataCollector
{
    using Google.Protobuf;
    using Nanomite.Common.Models.Base;
    using Nanomite.Core.DataAccess;
    using Nanomite.Core.Network;
    using Nanomite.Core.Network.Common;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the <see cref="DataHandler" />
    /// </summary>
    public class DataHandler
    {
        /// <summary>
        /// The service list
        /// </summary>
        private static Dictionary<string, NanomiteClient> serviceList = new Dictionary<string, NanomiteClient>();

        /// <summary>
        /// The constraints
        /// </summary>
        private static Dictionary<string, List<Constraint>> constraints = new Dictionary<string, List<Constraint>>();

        /// <summary>
        /// Registers a client for a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="client">The client.</param>
        public void RegisterServiceForType(Type type, NanomiteClient client)
        {
            if (!serviceList.ContainsKey(type.FullName))
            {
                serviceList.Add(type.FullName, client);
            }

            if (!constraints.ContainsKey(type.FullName))
            {
                constraints.Add(type.FullName, new List<Constraint>());
            }
        }

        /// <summary>
        /// Registers the constraint for a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="propertyName">Name of the property.</param>
        /// <param name="foreignKeyName">Name of the foreign key.</param>
        public void RegisterConstraint(Type type, string propertyName, string foreignKeyName)
        {
            if (!constraints[type.FullName].Any(q => q.PropertyName == propertyName))
            {
                Constraint c = new Constraint()
                {
                    PropertyName = propertyName,
                    TypeFullName = type.FullName,
                    ForeignKeyName = foreignKeyName
                };
                constraints[type.FullName].Add(c);
            }
        }

        /// <summary>
        /// Gets an entitiy by id. If include all is set to true, all relational data from other services will
        /// be included at the object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The identifier.</param>
        /// <param name="includeAll">if set to <c>true</c> [include all].</param>
        /// <returns>the requeted entity</returns>
        public async Task<T> GetById<T>(Guid id, bool includeAll = true) where T : class, IBaseModel
        {
            T result = default(T);
            if (CommonRepositoryHandler.GetRepository(typeof(T)) != null)
            {
                result = CommonRepositoryHandler.GetById(typeof(T), id, includeAll) as T;
            }

            return await GetRelationalData(result, includeAll);
        }

        /// <summary>
        /// Gets the relational data.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="result">The result.</param>
        /// <param name="includeAll">if set to <c>true</c> [include all].</param>
        /// <returns></returns>
        private async Task<T> GetRelationalData<T>(T result, bool includeAll) where T : class, IBaseModel
        {
            if (result != null && includeAll)
            {
                var properties = result.GetType().GetProperties().Where(p => p.CanWrite);
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(result);
                    Type protoType = null;
                    if ((value as IList) != null && (value as IList).Count > 0)
                    {
                        protoType = prop.PropertyType.GenericTypeArguments.FirstOrDefault();
                        if (constraints.ContainsKey(protoType.FullName)
                            && serviceList.ContainsKey(protoType.FullName))
                        {
                            var constraint = constraints[protoType.FullName].FirstOrDefault(q => q.PropertyName == prop.Name);
                            if (constraint != null)
                            {
                                var data = await GetConstraintsData(protoType, constraint.ForeignKeyName, result.Id.ToString());
                                foreach (var item in data)
                                {
                                    (value as IList).Add(item);
                                }
                            }
                        }
                    }
                    else
                    {
                        protoType = prop.PropertyType;
                        if (constraints.ContainsKey(protoType.FullName)
                            && serviceList.ContainsKey(protoType.FullName))
                        {
                            var constraint = constraints[protoType.FullName].FirstOrDefault(q => q.PropertyName == prop.Name);
                            if (constraint != null)
                            {
                                var constraintsProp = result.GetType().GetProperties().FirstOrDefault(p => p.Name == constraint.ForeignKeyName);
                                if (constraintsProp != null)
                                {
                                    var constraintsKey = constraintsProp.GetValue(result).ToString();
                                    var data = await GetConstraintsData(protoType, constraint.ForeignKeyName, constraintsKey);
                                    if (data != null)
                                    {
                                        prop.SetValue(result, data[0]);
                                    }
                                    else
                                    {
                                        prop.SetValue(result, null);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the constraints data.
        /// </summary>
        /// <param name="protoType">Type of the proto.</param>
        /// <param name="constraintsName">Name of the constraints.</param>
        /// <param name="constraintsKey">The constraints key.</param>
        /// <returns></returns>
        private async Task<IList> GetConstraintsData(Type protoType, string constraintsName, string constraintsKey)
        {
            if (constraints.ContainsKey(protoType.FullName) && serviceList.ContainsKey(protoType.FullName))
            {
                var client = serviceList[protoType.FullName];
                var filter = constraintsName + " eq " + constraintsKey;
                MethodInfo method = typeof(NanomiteClient).GetMethod(nameof(NanomiteClient.FetchData));
                MethodInfo genericMethod = method.MakeGenericMethod(protoType);
                return await (dynamic)genericMethod.Invoke(client, new object[] { filter, true }) as IList;
            }

            return null;
        }
    }
}
