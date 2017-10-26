﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LightInject;
using Moq;
using NUnit.Framework;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Exceptions;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Services;
using Umbraco.Web;
using Umbraco.Web.Models;
using Umbraco.Web.PropertyEditors;
using Umbraco.Web.PropertyEditors.ValueConverters;
using Umbraco.Web.PublishedCache;

namespace Umbraco.Tests.Facade
{
    [TestFixture]
    public class NestedContentTests
    {
        private (PublishedContentType, PublishedContentType) CreateContentTypes()
        {
            Current.Reset();

            var logger = Mock.Of<ILogger>();
            var profiler = Mock.Of<IProfiler>();
            var proflog = new ProfilingLogger(logger, profiler);

            var dataTypeService = new Mock<IDataTypeService>();

            // mocked dataservice returns nested content preValues
            dataTypeService
                .Setup(x => x.GetPreValuesCollectionByDataTypeId(It.IsAny<int>()))
                .Returns((int id) =>
                {
                    if (id == 1)
                        return new PreValueCollection(new Dictionary<string, PreValue>
                        {
                            { "minItems", new PreValue("1") },
                            { "maxItems", new PreValue("1") },
                            { "contentTypes", new PreValue("[{\"ncAlias\":\"contentN1\",\"ncTabAlias\":\"\",\"nameTemplate\":\"\"}]") }
                        });
                    if (id == 2)
                        return new PreValueCollection(new Dictionary<string, PreValue>
                        {
                            { "minItems", new PreValue("1") },
                            { "maxItems", new PreValue("99") },
                            { "contentTypes", new PreValue("[{\"ncAlias\":\"contentN1\",\"ncTabAlias\":\"\",\"nameTemplate\":\"\"}]") }
                        });
                    return null;
                });

            var publishedModelFactory = new Mock<IPublishedModelFactory>();

            // mocked model factory returns model type
            var modelTypes = new Dictionary<string, Type>
            {
                { "contentN1", typeof(TestElementModel) }
            };
            publishedModelFactory
                .Setup(x => x.MapModelType(It.IsAny<Type>()))
                .Returns((Type type) => ModelType.Map(type, modelTypes));

            // mocked model factory creates models
            publishedModelFactory
                .Setup(x => x.CreateModel(It.IsAny<IPublishedElement>()))
                .Returns((IPublishedElement element) =>
                {
                    if (element.ContentType.Alias.InvariantEquals("contentN1"))
                        return new TestElementModel(element);
                    return element;
                });

            // mocked model factory creates model lists
            publishedModelFactory
                .Setup(x => x.CreateModelList(It.IsAny<string>()))
                .Returns((string alias) =>
                {
                    return alias == "contentN1"
                        ? (IList) new List<TestElementModel>()
                        : (IList) new List<IPublishedElement>();
                });

            var contentCache = new Mock<IPublishedContentCache>();
            var facade = new Mock<IFacade>();

            // mocked facade returns a content cache
            facade
                .Setup(x => x.ContentCache)
                .Returns(contentCache.Object);

            var facadeAccessor = new Mock<IFacadeAccessor>();
            //container.RegisterSingleton(f => facadeAccessor.Object);

            // mocked facade accessor returns a facade
            facadeAccessor
                .Setup(x => x.Facade)
                .Returns(facade.Object);

            var converters = new PropertyValueConverterCollection(new IPropertyValueConverter[]
            {
                new NestedContentSingleValueConverter(facadeAccessor.Object, publishedModelFactory.Object, proflog),
                new NestedContentManyValueConverter(facadeAccessor.Object, publishedModelFactory.Object, proflog),
            });

            PropertyEditorCollection editors = null;
            editors = new PropertyEditorCollection(new PropertyEditor[]
            {
                new NestedContentPropertyEditor(Mock.Of<ILogger>(), new Lazy<PropertyEditorCollection>(() => editors))
            });

            var source = new DataTypeConfigurationSource(dataTypeService.Object, editors);
            var factory = new PublishedContentTypeFactory(publishedModelFactory.Object, converters, source);

            var propertyType1 = factory.CreatePropertyType("property1", 1, Constants.PropertyEditors.NestedContentAlias);
            var propertyType2 = factory.CreatePropertyType("property2", 2, Constants.PropertyEditors.NestedContentAlias);
            var propertyTypeN1 = factory.CreatePropertyType("propertyN1", 0, Constants.PropertyEditors.TextboxAlias);

            var contentType1 = factory.CreateContentType(1, "content1", new[] { propertyType1 });
            var contentType2 = factory.CreateContentType(2, "content2", new[] { propertyType2 });
            var contentTypeN1 = factory.CreateContentType(2, "contentN1", new[] { propertyTypeN1 });

            // mocked content cache returns content types
            contentCache
                .Setup(x => x.GetContentType(It.IsAny<string>()))
                .Returns((string alias) =>
                {
                    if (alias.InvariantEquals("contentN1")) return contentTypeN1;
                    return null;
                });

            return (contentType1, contentType2);
        }

        [Test]
        public void SingleNestedTest()
        {
            (var contentType1, _) = CreateContentTypes();

            // nested single converter returns the proper value clr type TestModel, and cache level
            Assert.AreEqual(typeof (TestElementModel), contentType1.GetPropertyType("property1").ClrType);
            Assert.AreEqual(PropertyCacheLevel.Content, contentType1.GetPropertyType("property1").CacheLevel);

            var key = Guid.NewGuid();
            var keyA = Guid.NewGuid();
            var content = new TestPublishedContent(contentType1, key, new[]
            {
                new TestPublishedProperty(contentType1.GetPropertyType("property1"), $@"[
                    {{ ""key"": ""{keyA}"", ""propertyN1"": ""foo"", ""ncContentTypeAlias"": ""contentN1"" }}
                ]")
            });
            var value = content.Value("property1");

            // nested single converter returns proper TestModel value
            Assert.IsInstanceOf<TestElementModel>(value);
            var valueM = (TestElementModel) value;
            Assert.AreEqual("foo", valueM.PropValue);
            Assert.AreEqual(keyA, valueM.Key);
        }

        [Test]
        public void ManyNestedTest()
        {
            (_, var contentType2) = CreateContentTypes();

            // nested many converter returns the proper value clr type IEnumerable<TestModel>, and cache level
            Assert.AreEqual(typeof (IEnumerable<TestElementModel>), contentType2.GetPropertyType("property2").ClrType);
            Assert.AreEqual(PropertyCacheLevel.Content, contentType2.GetPropertyType("property2").CacheLevel);

            var key = Guid.NewGuid();
            var keyA = Guid.NewGuid();
            var keyB = Guid.NewGuid();
            var content = new TestPublishedContent(contentType2, key, new[]
            {
                new TestPublishedProperty(contentType2.GetPropertyType("property2"), $@"[
                    {{ ""key"": ""{keyA}"", ""propertyN1"": ""foo"", ""ncContentTypeAlias"": ""contentN1"" }},
                    {{ ""key"": ""{keyB}"", ""propertyN1"": ""bar"", ""ncContentTypeAlias"": ""contentN1"" }}
                ]")
            });
            var value = content.Value("property2");

            // nested many converter returns proper IEnumerable<TestModel> value
            Assert.IsInstanceOf<IEnumerable<IPublishedElement>>(value);
            Assert.IsInstanceOf<IEnumerable<TestElementModel>>(value);
            var valueM = ((IEnumerable<TestElementModel>) value).ToArray();
            Assert.AreEqual("foo", valueM[0].PropValue);
            Assert.AreEqual(keyA, valueM[0].Key);
            Assert.AreEqual("bar", valueM[1].PropValue);
            Assert.AreEqual(keyB, valueM[1].Key);
        }

        public class TestElementModel : PublishedElementModel
        {
            public TestElementModel(IPublishedElement content)
                : base(content)
            { }

            public string PropValue => this.Value<string>("propertyN1");
        }

        class TestPublishedProperty : PublishedPropertyBase
        {
            private readonly bool _preview;
            private IPublishedElement _owner;

            public TestPublishedProperty(PublishedPropertyType propertyType, object source)
                : base(propertyType, PropertyCacheLevel.Content) // initial reference cache level always is .Content
            {
                SourceValue = source;
                HasValue = source != null && (!(source is string ssource) || !string.IsNullOrWhiteSpace(ssource));
            }

            public TestPublishedProperty(PublishedPropertyType propertyType, IPublishedElement element, bool preview, PropertyCacheLevel referenceCacheLevel, object source)
                : base(propertyType, referenceCacheLevel)
            {
                SourceValue = source;
                HasValue = source != null && (!(source is string ssource) || !string.IsNullOrWhiteSpace(ssource));
                _owner = element;
                _preview = preview;
            }

            private object InterValue => PropertyType.ConvertSourceToInter(null, SourceValue, false);

            internal void SetOwner(IPublishedElement owner)
            {
                _owner = owner;
            }

            public override bool HasValue { get; }
            public override object SourceValue { get; }
            public override object Value => PropertyType.ConvertInterToObject(_owner, ReferenceCacheLevel, InterValue, _preview);
            public override object XPathValue => throw new WontImplementException();
        }

        class TestPublishedContent : PublishedContentBase
        {
            public TestPublishedContent(PublishedContentType contentType, Guid key, IEnumerable<TestPublishedProperty> properties)
            {
                ContentType = contentType;
                Key = key;
                var propertiesA = properties.ToArray();
                Properties = propertiesA;
                foreach (var property in propertiesA)
                    property.SetOwner(this);
            }

            // ReSharper disable UnassignedGetOnlyAutoProperty
            public override PublishedItemType ItemType { get; }
            public override bool IsDraft { get; }
            public override IPublishedContent Parent { get; }
            public override IEnumerable<IPublishedContent> Children { get; }
            public override PublishedContentType ContentType { get; }
            // ReSharper restore UnassignedGetOnlyAutoProperty

            // ReSharper disable UnassignedGetOnlyAutoProperty
            public override int Id { get; }
            public override int TemplateId { get; }
            public override int SortOrder { get; }
            public override string Name { get; }
            public override string UrlName { get; }
            public override string DocumentTypeAlias { get; }
            public override int DocumentTypeId { get; }
            public override string WriterName { get; }
            public override string CreatorName { get; }
            public override int WriterId { get; }
            public override int CreatorId { get; }
            public override string Path { get; }
            public override DateTime CreateDate { get; }
            public override DateTime UpdateDate { get; }
            public override Guid Version { get; }
            public override int Level { get; }
            public override Guid Key { get; }
            // ReSharper restore UnassignedGetOnlyAutoProperty

            public override IEnumerable<IPublishedProperty> Properties { get; }
            public override IPublishedProperty GetProperty(string alias)
            {
                return Properties.FirstOrDefault(x => x.PropertyTypeAlias.InvariantEquals(alias));
            }
        }
    }
}
