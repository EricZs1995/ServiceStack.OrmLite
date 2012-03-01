//
// ServiceStack.OrmLite: Light-weight POCO ORM for .NET and Mono
//
// Authors:
//   Demis Bellot (demis.bellot@gmail.com)
//
// Copyright 2010 Liquidbit Ltd.
//
// Licensed under the same terms of ServiceStack: new BSD license.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using ServiceStack.Common.Extensions;
using ServiceStack.DataAnnotations;
using ServiceStack.Text;

namespace ServiceStack.OrmLite
{
	internal static class OrmLiteConfigExtensions
	{
		public const string IdField = "Id";

		private static Dictionary<Type, ModelDefinition> typeModelDefinitionMap = new Dictionary<Type, ModelDefinition>();

		private static bool IsNullableType(Type theType)
		{
			return (theType.IsGenericType
					&& theType.GetGenericTypeDefinition().Equals(typeof(Nullable<>)));
		}

		internal static bool CheckForIdField(IEnumerable<PropertyInfo> objProperties)
		{
			// Not using Linq.Where() and manually iterating through objProperties just to avoid dependencies on System.Xml??
			foreach (var objProperty in objProperties)
			{
				if (objProperty.Name != IdField) continue;
				return true;
			}
			return false;
		}

		internal static void ClearCache()
		{
			typeModelDefinitionMap = new Dictionary<Type, ModelDefinition>();
		}

		internal static ModelDefinition GetModelDefinition(this Type modelType)
		{
            ModelDefinition modelDef;
            
            if (typeModelDefinitionMap.TryGetValue(modelType, out modelDef))
                return modelDef;

            var modelAliasAttr = modelType.FirstAttribute<AliasAttribute>();
		    var schemaAttr = modelType.FirstAttribute<SchemaAttribute>();
            modelDef = new ModelDefinition
            {
                ModelType = modelType,
                Name = modelType.Name,
                Alias = modelAliasAttr != null ? modelAliasAttr.Name : null,
                Schema = schemaAttr != null ? schemaAttr.Name : null
            };

            modelDef.CompositeIndexes.AddRange(
                modelType.GetCustomAttributes(typeof(CompositeIndexAttribute), true).ToList()
                .ConvertAll(x => (CompositeIndexAttribute)x));

            var objProperties = modelType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance).ToList();

            var hasIdField = CheckForIdField(objProperties);

            var i = 0;
            foreach (var propertyInfo in objProperties)
            {
				if (propertyInfo.FirstAttribute<IgnoreAttribute>()!=null) continue;
				var sequenceAttr = propertyInfo.FirstAttribute<SequenceAttribute>();
				var computeAttr= propertyInfo.FirstAttribute<ComputeAttribute>();
				var pkAttribute = propertyInfo.FirstAttribute<PrimaryKeyAttribute>();
				var decimalAttribute = propertyInfo.FirstAttribute<DecimalLengthAttribute>();
                var isFirst = i++ == 0;

                var isPrimaryKey = propertyInfo.Name == IdField || (!hasIdField && isFirst)
					|| pkAttribute != null;

                var isNullableType = IsNullableType(propertyInfo.PropertyType);

                var isNullable = (!propertyInfo.PropertyType.IsValueType
                                   && propertyInfo.FirstAttribute<RequiredAttribute>() == null)
                                 || isNullableType;

                var propertyType = isNullableType
                    ? Nullable.GetUnderlyingType(propertyInfo.PropertyType)
                    : propertyInfo.PropertyType;

                var aliasAttr = propertyInfo.FirstAttribute<AliasAttribute>();

                var indexAttr = propertyInfo.FirstAttribute<IndexAttribute>();
                var isIndex = indexAttr != null;
                var isUnique = isIndex && indexAttr.Unique;

                var stringLengthAttr = propertyInfo.FirstAttribute<StringLengthAttribute>();

                var defaultValueAttr = propertyInfo.FirstAttribute<DefaultAttribute>();

                var referencesAttr = propertyInfo.FirstAttribute<ReferencesAttribute>();
				
				if(decimalAttribute != null  && stringLengthAttr==null)
					stringLengthAttr= new StringLengthAttribute(decimalAttribute.Precision);
				
                var fieldDefinition = new FieldDefinition
                {
                    Name = propertyInfo.Name,
                    Alias = aliasAttr != null ? aliasAttr.Name : null,
                    FieldType = propertyType,
                    PropertyInfo = propertyInfo,
                    IsNullable = isNullable,
                    IsPrimaryKey = isPrimaryKey,
                    AutoIncrement = isPrimaryKey && propertyInfo.FirstAttribute<AutoIncrementAttribute>() != null,
                    IsIndexed = isIndex,
                    IsUnique = isUnique,
                    FieldLength = stringLengthAttr != null ? stringLengthAttr.MaximumLength : (int?)null,
                    DefaultValue = defaultValueAttr != null ? defaultValueAttr.DefaultValue : null,
                    ReferencesType = referencesAttr != null ? referencesAttr.Type : null,
                    ConvertValueFn = OrmLiteConfig.DialectProvider.ConvertDbValue,
                    QuoteValueFn = OrmLiteConfig.DialectProvider.GetQuotedValue,
                    GetValueFn = propertyInfo.GetPropertyGetterFn(),
                    SetValueFn = propertyInfo.GetPropertySetterFn(),
					Sequence= sequenceAttr!=null?sequenceAttr.Name:string.Empty,
					IsComputed= computeAttr!=null,
					ComputeExpression=  computeAttr!=null? computeAttr.Expression: string.Empty,
					Scale = decimalAttribute != null ? decimalAttribute.Scale : (int?)null,
                };

                modelDef.FieldDefinitions.Add(fieldDefinition);
            }

		    modelDef.SqlSelectAllFromTable = "SELECT {0} FROM {1} ".Fmt(OrmLiteConfig.DialectProvider.GetColumnNames(modelDef),
		                                                                OrmLiteConfig.DialectProvider.GetQuotedTableName(
		                                                                    modelDef));
            Dictionary<Type, ModelDefinition> snapshot, newCache;
            do
            {
                snapshot = typeModelDefinitionMap;
                newCache = new Dictionary<Type, ModelDefinition>(typeModelDefinitionMap);
                newCache[modelType] = modelDef;

            } while (!ReferenceEquals(
                Interlocked.CompareExchange(ref typeModelDefinitionMap, newCache, snapshot), snapshot));

            return modelDef;
		}

	}
}