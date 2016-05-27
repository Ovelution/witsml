﻿//----------------------------------------------------------------------- 
// PDS.Witsml.Server, 2016.1
//
// Copyright 2016 Petrotechnical Data Systems
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//   
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using Energistics.DataAccess;
using Energistics.Datatypes;
using MongoDB.Driver;
using PDS.Framework;
using PDS.Witsml.Data;

namespace PDS.Witsml.Server.Data
{
    /// <summary>
    /// Encloses MongoDb update method and its helper methods
    /// </summary>
    /// <typeparam name="T">The data object type.</typeparam>
    /// <seealso cref="PDS.Witsml.Data.DataObjectNavigator{MongoDbUpdateContext}" />
    public class MongoDbUpdate<T> : DataObjectNavigator<MongoDbUpdateContext<T>>
    {
        private readonly IMongoCollection<T> _collection;
        private readonly WitsmlQueryParser _parser;
        private readonly string _idPropertyName;

        private FilterDefinition<T> _entityFilter;
        private T _entity;

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoDbUpdate{T}"/> class.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="parser">The parser.</param>
        /// <param name="idPropertyName">Name of the identifier property.</param>
        /// <param name="ignored">The ignored.</param>
        public MongoDbUpdate(IMongoCollection<T> collection, WitsmlQueryParser parser, string idPropertyName = "Uid", List<string> ignored = null) : base(new MongoDbUpdateContext<T>())
        {
            Context.Ignored = ignored;

            _collection = collection;
            _parser = parser;
            _idPropertyName = idPropertyName;
        }

        /// <summary>
        /// Updates the specified entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="uri">The URI.</param>
        /// <param name="updates">The updates.</param>
        public void Update(T entity, EtpUri uri, Dictionary<string, object> updates)
        {
             _entityFilter = MongoDbUtility.GetEntityFilter<T>(uri, _idPropertyName);
            _entity = entity;

            Context.Update = Update(updates, uri.ObjectId);
            BuildUpdate(_parser.Element());

            if (Logger.IsDebugEnabled)
            {
                var updateJson = Context.Update.Render(_collection.DocumentSerializer, _collection.Settings.SerializerRegistry);
                Logger.DebugFormat("Detected update elements: {0}", updateJson);
            }

            _collection.UpdateOne(_entityFilter, Context.Update);
        }

        /// <summary>
        /// Creates an <see cref="UpdateDefinition{T}"/> based on the supplied dictionary of name/value pairs.
        /// </summary>
        /// <param name="updates">The updates.</param>
        /// <param name="uidValue">The uid value.</param>
        /// <returns>The update definition.</returns>
        public UpdateDefinition<T> Update(Dictionary<string, object> updates, string uidValue)
        {
            var update = Builders<T>.Update.Set(_idPropertyName, uidValue);

            foreach (var pair in updates)
                update = update.Set(pair.Key, pair.Value);

            return update;
        }

        /// <summary>
        /// Updates a document using the specified filter and update definitions.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="update">The update.</param>
        public void UpdateFields(FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            if (Logger.IsDebugEnabled)
            {
                var filterJson = filter.Render(_collection.DocumentSerializer, _collection.Settings.SerializerRegistry);
                var updateJson = update.Render(_collection.DocumentSerializer, _collection.Settings.SerializerRegistry);
                Logger.DebugFormat("Detected update parameters: {0}", updateJson);
                Logger.DebugFormat("Detected update filters: {0}", filterJson);
            }

            _collection.UpdateOne(filter, update);
        }

        /// <summary>
        /// Navigates the element group.
        /// </summary>
        /// <param name="propertyInfo">The property information.</param>
        /// <param name="elements">The elements.</param>
        /// <param name="parentPath">The parent path.</param>
        protected override void NavigateElementGroup(PropertyInfo propertyInfo, IGrouping<string, XElement> elements, string parentPath)
        {
            PushPropertyInfo(propertyInfo);
            base.NavigateElementGroup(propertyInfo, elements, parentPath);
            PopPropertyInfo();
        }

        /// <summary>
        /// Navigates the nullable element type.
        /// </summary>
        /// <param name="elementType">Type of the element.</param>
        /// <param name="element">The element.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyInfo">The property information.</param>
        protected override void NavigateNullableElementType(Type elementType, XElement element, string propertyPath, PropertyInfo propertyInfo)
        {
            NavigateElementType(elementType, element, propertyPath);

            if (propertyInfo.DeclaringType?.GetProperty(propertyInfo.Name + "Specified") != null)
                Context.Update = Context.Update.Set(propertyPath + "Specified", true);                
        }

        /// <summary>
        /// Navigates the recurring elements.
        /// </summary>
        /// <param name="elements">The elements.</param>
        /// <param name="childType">Type of the child.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyInfo">The property information.</param>
        protected override void NavigateRecurringElements(List<XElement> elements, Type childType, string propertyPath, PropertyInfo propertyInfo)
        {
            NavigateArrayElementType(elements, childType, null, propertyPath, propertyInfo);
        }

        /// <summary>
        /// Navigates the array element type.
        /// </summary>
        /// <param name="elements">The elements.</param>
        /// <param name="childType">Type of the child.</param>
        /// <param name="element">The element.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyInfo">The property information.</param>
        protected override void NavigateArrayElementType(List<XElement> elements, Type childType, XElement element, string propertyPath, PropertyInfo propertyInfo)
        {
            UpdateArrayElements(elements, propertyInfo, Context.PropertyValues.Last(), childType, propertyPath);
        }

        /// <summary>
        /// Navigates the uom attribute.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="measureValue">The measure value.</param>
        /// <param name="uomValue">The uom value.</param>
        /// <exception cref="WitsmlException"></exception>
        protected override void NavigateUomAttribute(XObject xmlObject, Type propertyType, string propertyPath, string measureValue, string uomValue)
        {
            // Throw error -446 if uomValue is specified but measureValue is missing or NaN
            if (!string.IsNullOrWhiteSpace(uomValue) && (string.IsNullOrWhiteSpace(measureValue) || measureValue.EqualsIgnoreCase("NaN")))
                throw new WitsmlException(ErrorCodes.MissingMeasureDataForUnit);

            NavigateProperty(xmlObject, propertyType, propertyPath, uomValue);
        }

        /// <summary>
        /// Handles the string value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        protected override void HandleStringValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue)
        {
            Context.Update = Context.Update.Set(propertyPath, propertyValue);
        }

        /// <summary>
        /// Handles the date time value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        /// <param name="dateTimeValue">The date time value.</param>
        protected override void HandleDateTimeValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue, DateTime dateTimeValue)
        {
            Context.Update = Context.Update.Set(propertyPath, dateTimeValue);
        }

        /// <summary>
        /// Handles the timestamp value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        /// <param name="timestampValue">The timestamp value.</param>
        protected override void HandleTimestampValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue, Timestamp timestampValue)
        {
            Context.Update = Context.Update.Set(propertyPath, timestampValue);
        }

        /// <summary>
        /// Handles the object value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        /// <param name="objectValue">The object value.</param>
        protected override void HandleObjectValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue, object objectValue)
        {
            Context.Update = Context.Update.Set(propertyPath, objectValue);
        }

        /// <summary>
        /// Handles the null value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        protected override void HandleNullValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue)
        {
            UnsetProperty(propertyPath);
        }

        /// <summary>
        /// Handles the na n value.
        /// </summary>
        /// <param name="xmlObject">The XML object.</param>
        /// <param name="propertyType">Type of the property.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <param name="propertyValue">The property value.</param>
        protected override void HandleNaNValue(XObject xmlObject, Type propertyType, string propertyPath, string propertyValue)
        {
            UnsetProperty(propertyPath);
        }

        private void BuildUpdate(XElement element)
        {
            NavigateElement(element, Context.DataObjectType);
        }

        private void PushPropertyInfo(PropertyInfo propertyInfo)
        {
            var propertyValue = Context.PropertyValues.Count == 0
                ? propertyInfo.GetValue(_entity)
                : propertyInfo.GetValue(Context.PropertyValues.Last());

            PushPropertyInfo(propertyInfo, propertyValue);
        }

        private void PushPropertyInfo(PropertyInfo propertyInfo, object propertyValue)
        {
            Context.PropertyInfos.Add(propertyInfo);
            Context.PropertyValues.Add(propertyValue);
        }

        private void PopPropertyInfo()
        {
            Context.PropertyInfos.Remove(Context.PropertyInfos.Last());
            Context.PropertyValues.Remove(Context.PropertyValues.Last());
        }

        private void UnsetProperty(string propertyPath)
        {
            if (Context.PropertyInfos.Last().IsDefined(typeof(RequiredAttribute), false))
                throw new WitsmlException(ErrorCodes.MissingRequiredData);

            Context.Update = Context.Update.Unset(propertyPath);
        }

        private void UpdateArrayElements(List<XElement> elements, PropertyInfo propertyInfo, object propertyValue, Type type, string parentPath)
        {
            var updateBuilder = Builders<T>.Update;
            var filterBuilder = Builders<T>.Filter;
            var idField = MongoDbUtility.LookUpIdField(type);
            var filterPath = GetPropertyPath(parentPath, idField);
            var properties = GetPropertyInfo(type);
            var positionPath = parentPath + ".$";
            
            foreach (var element in elements)
            {
                var elementId = GetElementId(element, idField);
                if (string.IsNullOrEmpty(elementId))
                    continue;

                var filters = new List<FilterDefinition<T>>() { _entityFilter };
                var current = GetCurrentElementValue(idField, elementId, properties, propertyValue);

                if (current == null)
                {
                    ValidateArrayElement(element, properties);

                    // update element name to match XSD type
                    var xmlType = type.GetCustomAttribute<XmlTypeAttribute>();
                    element.Name = xmlType != null ? xmlType.TypeName : element.Name;

                    var item = WitsmlParser.Parse(type, element.ToString());

                    var update = updateBuilder.Push(parentPath, item);
                    _collection.UpdateOne(filterBuilder.And(filters), update);
                }
                else
                {
                    ValidateArrayElement(element, properties, false);

                    var elementFilter = Builders<T>.Filter.EqIgnoreCase(filterPath, elementId);
                    filters.Add(elementFilter);
                    var filter = filterBuilder.And(filters);

                    var update = updateBuilder.Set(GetPropertyPath(positionPath, idField), elementId);

                    var saveUpdate = Context.Update;
                    Context.Update = update;
                  
                    PushPropertyInfo(propertyInfo, current);
                    NavigateElement(element, type, positionPath);
                    PopPropertyInfo();

                    if (Logger.IsDebugEnabled)
                    {
                        var filterJson = filter.Render(_collection.DocumentSerializer, _collection.Settings.SerializerRegistry);
                        var updateJson = update.Render(_collection.DocumentSerializer, _collection.Settings.SerializerRegistry);
                        Logger.DebugFormat("Detected update parameters: {0}", updateJson);
                        Logger.DebugFormat("Detected update filters: {0}", filterJson);
                    }

                    _collection.UpdateOne(filter, Context.Update);
                    Context.Update = saveUpdate;                   
                }
            }
        }

        private string GetElementId(XElement element, string idField)
        {
            var idAttribute = element.Attributes().FirstOrDefault(a => a.Name.LocalName.EqualsIgnoreCase(idField));
            if (idAttribute != null)
                return idAttribute.Value.Trim();

            var idElement = element.Elements().FirstOrDefault(e => e.Name.LocalName.EqualsIgnoreCase(idField));
            if (idElement != null)
                return idElement.Value.Trim();

            return string.Empty;
        }

        private object GetCurrentElementValue(string idField, string elementId, IList<PropertyInfo> properties, object propertyValue)
        {
            var list = (IEnumerable)propertyValue;
            foreach (var item in list)
            {
                var prop = GetPropertyInfoForAnElement(properties, idField);
                var idValue = prop.GetValue(item).ToString();
                if (elementId.EqualsIgnoreCase(idValue))
                    return item;
            }
            return null;
        }

        private void ValidateArrayElement(XElement element, IList<PropertyInfo> properties, bool isAdd = true)
        {          
            if (isAdd)
            {
                WitsmlParser.RemoveEmptyElements(element);

                if (!element.HasElements)
                    throw new WitsmlException(ErrorCodes.EmptyNewElementsOrAttributes);
            }
            else
            {
                var emptyElements = element.Elements()
                    .Where(e => e.IsEmpty || string.IsNullOrWhiteSpace(e.Value))
                    .ToList();

                foreach (var child in emptyElements)
                {
                    var prop = GetPropertyInfoForAnElement(properties, child.Name.LocalName);
                    if (prop == null) continue;

                    if (prop.IsDefined(typeof(RequiredAttribute), false))
                        throw new WitsmlException(ErrorCodes.EmptyNewElementsOrAttributes);
                }

                var emptyAttributes = element.Attributes()
                    .Where(a => string.IsNullOrWhiteSpace(a.Value))
                    .ToList();

                foreach (var child in emptyAttributes)
                {
                    var prop = GetPropertyInfoForAnElement(properties, child.Name.LocalName);
                    if (prop == null) continue;

                    if (prop.IsDefined(typeof(RequiredAttribute), false))
                        throw new WitsmlException(ErrorCodes.MissingRequiredData);
                }
            }
        }
    }
}
