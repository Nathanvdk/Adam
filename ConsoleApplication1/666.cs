using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Data.SqlClient;
using System.Linq;
using System.Xml.Linq;
using Adam.Core.DatabaseManagerLibrary;
using Adam.Tools.LogHandler;

namespace Adam.Core.DatabaseManager
{
	public class Upgrader : UpgradeScriptBase
	{
		private SettingRepository<SystemSetting> _settingRepository;
		private SettingRepository<SiteSetting> _siteSettingRepository;
		private SiteRepository _siteRepository;
		private List<RenamedFacet> _renamedFacets;

		private readonly Guid _assetStudioRegisteredSpaces = new Guid("2D91E63F-6A6C-49F7-A305-A15E01355017");
		private readonly Guid _assetStudioRegisteredFacets = new Guid("7494B9E3-9A2F-4A1B-A82B-4DAFCE317FAC");

		protected override void Run()
		{
			try
			{
				var databaseContext = new DataContext(Connection);
				_settingRepository = new SettingRepository<SystemSetting>(databaseContext);
				_siteSettingRepository = new SettingRepository<SiteSetting>(databaseContext);
				_siteRepository = new SiteRepository(databaseContext);
				_renamedFacets = new List<RenamedFacet>();

				UpdateFacetSettings();
				databaseContext.SubmitChanges();
			}
			catch (Exception exception)
			{
				LogManager.Write(LogSeverity.Error, exception);
			}
		}

		private void UpdateFacetSettings()
		{
			//Read the property facetProviderSettingName of al registered spaces
			var sites = _siteRepository.SelectAll();
			var siteSettings = _siteSettingRepository.SelectAll().ToList();
			var registeredSpacesSetting = _settingRepository.SelectSettingById(_assetStudioRegisteredSpaces);
			var registeredFacetsSetting = _settingRepository.SelectSettingById(_assetStudioRegisteredFacets);
			var allRegisteredFacets = SelectAddElementFromSetting(registeredFacetsSetting.DefaultValue).ToList();

			foreach (var site in sites)
			{
				var registeredSpaceSiteSetting = siteSettings.FirstOrDefault(s => s.SiteId == site.ID && s.Name == registeredSpacesSetting.Name);
				if (registeredSpaceSiteSetting == null) continue;

				var allRegisteredSpaces = SelectAddElementFromSetting(registeredSpaceSiteSetting.Value).ToList();
				foreach (var registeredSpace in allRegisteredSpaces)
				{
					//Parse the setting from the property facetProviderSettingName.
					var facetProviderSettingName = ParseFacetProviderSettingName(registeredSpace);
					var facets = _settingRepository.SelectSettingsByName(facetProviderSettingName).First();
					var valueToUse = facets.Value != facets.DefaultValue ? facets.Value : facets.DefaultValue;

					//Move all the facet types to a new setting called .assetStudioRegisteredFacets
					var facetsUsedInCurrentSpace = SelectAddElementFromSetting(valueToUse).ToList();
					allRegisteredFacets.AddRange(facetsUsedInCurrentSpace);

					//filter the doubles.
					allRegisteredFacets = FilterDoubles(allRegisteredFacets);

					var allRegisteredFacetNames = SelectAllFacetNames(facetsUsedInCurrentSpace);

					//7)Check if showclassification is true in the setting, if so add a link to ClassificationTreeFilter at te beginning of the facets attribute.
					var showClassificationAttr = registeredSpace.AdamAttribute("showClassificationTree");
					if (showClassificationAttr != null)
					{
						if (showClassificationAttr.Value.ToLowerInvariant() == Boolean.TrueString.ToLowerInvariant())
						{
							allRegisteredFacetNames.Insert(0, "ClassificationTreeFilter");
						}

						showClassificationAttr.Remove();
					}

					//Check if ShowFilterBox is true in the setting, if so add a link to TextFilter after the ClassificationTreeFilter in the facets attribute.
					var showFilterBoxAttr = registeredSpace.AdamAttribute("showFilterBox");
					if (showFilterBoxAttr != null)
					{
						if (showFilterBoxAttr.Value.ToLowerInvariant() == Boolean.TrueString.ToLowerInvariant())
						{
							allRegisteredFacetNames.Insert(showClassificationAttr != null && showClassificationAttr.Value == Boolean.TrueString ? 1 : 0, "TextFilter");
						}
						showFilterBoxAttr.Remove();
					}

					//add a new attribute facets with a link to the newly registered facets.

					registeredSpace.SetAttributeValue("facets", string.Join(", ", allRegisteredFacetNames.ToArray()));

					registeredSpace.AdamAttribute("facetProviderSettingName").Remove();
					registeredSpace.AdamAttribute("widgetLocation").Remove();
					registeredSpace.AdamAttribute("widgetSize").Remove();

					//reset renamed facets.
					_renamedFacets = new List<RenamedFacet>();
				}

				//Add TextFilter and ClassificationTreeFilter to the facet types
				allRegisteredFacets.Add(XElement.Parse("<add name='ClassificationTreeFilter' type='Adam.Web.AssetStudio.Controls.Filters.ClassificationTreeFilterFacetControl, Adam.Web.AssetStudio' rootIdentifierMode='ContextAware' />"));
				allRegisteredFacets.Add(XElement.Parse("<add name='TextFilter' type='Adam.Web.AssetStudio.Controls.Filters.TextFilterFacetControl, Adam.Web.AssetStudio' />"));

				allRegisteredFacets = FilterDoubles(allRegisteredFacets);

				//save the registerdSpacesSetting
				registeredSpaceSiteSetting.Value = ParseResultToSetting(allRegisteredSpaces, "<registeredSpaces />");

				//Save the new registeredFacet setting.
				var registeredFacetSiteSetting = _siteSettingRepository.SelectAll().FirstOrDefault(s => s.SiteId == site.ID && s.Name == registeredFacetsSetting.Name) ??
						  new SiteSetting { Name = registeredFacetsSetting.Name, SiteId = site.ID };
				registeredFacetSiteSetting.Value = ParseResultToSetting(allRegisteredFacets, string.IsNullOrEmpty(registeredFacetsSetting.Value) ? "<registeredFacets></registeredFacets>" : registeredFacetsSetting.Value);
				if (registeredFacetSiteSetting.ID == Guid.Empty)
				{
					_siteSettingRepository.Insert(registeredFacetSiteSetting);
				}
			}
		}

		private string ParseResultToSetting(IEnumerable<XElement> allRegisteredFacets, string settingValue)
		{
			var document = XDocument.Parse(settingValue);
			foreach (var allRegisteredFacet in allRegisteredFacets)
			{
				document.Root.Add(allRegisteredFacet);
			}

			return document.ToString(SaveOptions.None);
		}

		private List<string> SelectAllFacetNames(IEnumerable<XElement> facetsUsedInCurrentSpace)
		{
			//return facetsUsedInCurrentSpace.Select(xElement => xElement.AdamAttribute("facetName").Value).ToList();
			var facetNames = new List<string>();
			foreach (var xElement in facetsUsedInCurrentSpace)
			{
				var facetName = xElement.AdamAttribute("name").Value;
				foreach (var renamedFacet in _renamedFacets.Where(renamedFacet => renamedFacet.OldName == facetName))
				{
					xElement.SetAttributeValue("name", renamedFacet.NewName);
				}

				facetNames.Add(xElement.AdamAttribute("name").Value);
			}

			return facetNames;
		}

		private IEnumerable<XElement> SelectAddElementFromSetting(string setting)
		{
			var addElements = XDocument.Parse(setting).Descendants("add").ToList();

			//order all attribute alphabeticly for filtering out the doubles.
			foreach (var element in addElements)
			{
				var attrs = element.Attributes().ToList();

				var facectNameAttr = element.AdamAttribute("facetName");
				if (facectNameAttr != null)
				{
					element.SetAttributeValue("name", facectNameAttr.Value);
				}


				attrs.Remove();
				attrs.Sort((a, b) => String.Compare(a.Name.LocalName, b.Name.LocalName, StringComparison.Ordinal));

				
				element.Add(attrs);
			}

			return addElements;
		}

		private string ParseFacetProviderSettingName(XElement value)
		{
			return value.AdamAttribute("facetProviderSettingName").Value;
		}

		private List<XElement> FilterDoubles(IReadOnlyCollection<XElement> allFacets)
		{
			//Filter out the doubles. I do this through string comparison, because this is to most easy to implement.
			var names = new List<string>();

			var stringifiedList = new List<String>(allFacets.Count);
			stringifiedList.AddRange(allFacets.Select(xElement => xElement.ToString()));
			stringifiedList = stringifiedList.Distinct().ToList();

			var returnList = stringifiedList.Select(XElement.Parse).ToList();

			for (var index = 0; index < returnList.Count; index++)
			{
				var xElement = returnList[index];
				//make sure that name is unique.
				var nameAttr = xElement.AdamAttribute("name");
				if (nameAttr == null) continue;

				var nameValue = nameAttr.Value;
				if (names.Contains(nameValue))
				{
					var newName = string.Format("{0}_{1}", nameValue, index);
					xElement.SetAttributeValue("name", newName);
					_renamedFacets.Add(new RenamedFacet {NewName =  newName, OldName =  nameValue});
				}
				names.Add(nameValue);
			}
			//parse back to XElement.
			return returnList;
		}

		public void Update(SqlConnection sqlConnection)
		{
			var databaseContext = new DataContext(sqlConnection);
			_settingRepository = new SettingRepository<SystemSetting>(databaseContext);
			_siteSettingRepository = new SettingRepository<SiteSetting>(databaseContext);
			_siteRepository = new SiteRepository(databaseContext);
			_renamedFacets = new List<RenamedFacet>();

			UpdateFacetSettings();

			databaseContext.SubmitChanges();
		}

		internal class SiteRepository
		{
			private readonly DataContext _databaseContext;

			public SiteRepository(DataContext databaseContext)
			{
				_databaseContext = databaseContext;
			}

			public IEnumerable<Site> SelectAll()
			{
				return _databaseContext.GetTable<Site>();
			}
		}

		internal class SettingRepository<T> where T : class,IAdamSetting
		{
			private readonly DataContext _databaseContext;

			public SettingRepository(DataContext databaseContext)
			{
				_databaseContext = databaseContext;
			}

			public T SelectSettingById(Guid id)
			{
				return SelectAll().FirstOrDefault(setting => setting.ID == id);
			}

			public IEnumerable<T> SelectSettingsByName(string name)
			{
				return SelectAll().Where(setting => setting.Name == name);
			}

			public IEnumerable<T> SelectAll()
			{
				return _databaseContext.GetTable<T>();
			}

			public void Insert(T setting)
			{
				setting.ID = Guid.NewGuid();
				_databaseContext.GetTable<T>().InsertOnSubmit(setting);
			}

		}

		internal interface IAdamSetting
		{
			Guid ID { get; set; }
			string Name { get; set; }
			string Value { get; set; }
		}

		[Table(Name = "tblSETTINGS")]
		internal class SystemSetting : IAdamSetting
		{
			[Column]
			public string Kind { get; set; }

			[Column]
			public string DefaultValue { get; set; }

			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public string Name { get; set; }

			[Column]
			public string Value { get; set; }
		}

		[Table(Name = "tblUSERSETTINGS")]
		internal class UserSetting : IAdamSetting
		{
			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public string Name { get; set; }

			[Column]
			public string Value { get; set; }
		}

		[Table(Name = "tblSITESETTINGS")]
		internal class SiteSetting : IAdamSetting
		{
			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public Guid SiteId { get; set; }

			[Column]
			public string Name { get; set; }

			[Column]
			public string Value { get; set; }
		}

		[Table(Name = "tblUSERGROUPSETTINGS")]
		internal class UserGroupSetting : IAdamSetting
		{
			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public string Name { get; set; }

			[Column]
			public string Value { get; set; }
		}

		[Table(Name = "tblSITES")]
		internal class Site
		{
			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public string Name { get; set; }
		}

		internal class RenamedFacet
		{
			public string OldName { get; set; }
			public string NewName { get; set; }
		}
	}

	public static class XElementExtensions
	{
		public static XAttribute AdamAttribute(this XElement element, XName name)
		{
			var el = element.Attribute(name);
			if (el != null)
				return el;

			var elements = element.Attributes().Where(e => e.Name.LocalName.ToString().ToLowerInvariant() == name.ToString().ToLowerInvariant()).ToList();
			return !elements.Any() ? null : elements.First();
		}
	}

}
