using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Adam.Core.DatabaseManagerLibrary;
using Adam.Tools.LogHandler;

namespace Adam.Core.DatabaseManager
{
	public class Upgrader665 : UpgradeScriptBase
	{
		private const string _supportedType = "Adam.Web.AssetStudio.Spaces.ClassificationSpace,Adam.Web.AssetStudio";

		private const string _classificationSpaceWidgetType = "Adam.Web.AssetStudio.Spaces.ClassificationSpaceWidget, Adam.Web.AssetStudio";

		private readonly Guid _assetStudioRegisteredSpaces = new Guid("2D91E63F-6A6C-49F7-A305-A15E01355017");
		private readonly Guid _assetStudioSpaces = new Guid("bf041b97-3671-472a-b7c5-cd13ecb53894");
		private readonly Guid _registeredAssetStudioWidgetsId = new Guid("6CA90AAE-2A38-4DDD-B4AA-673945835348");

		private SettingRepository<SystemSetting> _settingRepository;
		private SettingRepository<SiteSetting> _siteSettingRepository;
		private SettingRepository<UserGroupSetting> _userGroupSettingRepository;
		private SettingRepository<UserSetting> _userSettingRepository;
		private SiteRepository _siteRepository;

		protected override void Run()
		{
			try
			{
				var databaseContext = new DataContext(Connection);
				_settingRepository = new SettingRepository<SystemSetting>(databaseContext);
				_siteSettingRepository = new SettingRepository<SiteSetting>(databaseContext);
				_userSettingRepository = new SettingRepository<UserSetting>(databaseContext);
				_userGroupSettingRepository = new SettingRepository<UserGroupSetting>(databaseContext);
				_siteRepository = new SiteRepository(databaseContext);

				UpdateSpaceSetting();
				databaseContext.SubmitChanges();
			}
			catch (Exception exception)
			{
				LogManager.Write(LogSeverity.Error, exception);
			}
		}

		private void UpdateSpaceSetting()
		{
			var systemSetting = _settingRepository.SelectSettingById(_assetStudioSpaces);
			var userSettings = _userSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();
			var siteSettings = _siteSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();

			var userGroupSettings = _userGroupSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();
			var sites = _siteRepository.SelectAll();

			var systemSpaces = SelectAllSpacesFromSetting(systemSetting);
			var siteSpaces = SelectAllSpacesFromUserSetting(siteSettings);
			var userSpaces = SelectAllSpacesFromUserSetting(userSettings);
			var userGroupSpace = SelectAllSpacesFromUserSetting(userGroupSettings);

			var allSpaces = new List<XElement>(systemSpaces);
			allSpaces.AddRange(siteSpaces);
			allSpaces.AddRange(userSpaces);
			allSpaces.AddRange(userGroupSpace);

			allSpaces = FilterDoubles(allSpaces, false);
		
			//add all the spaces to a new setting called registeredSpaces setting.
			var registeredSpacesSetting = _settingRepository.SelectSettingById(_assetStudioRegisteredSpaces);
			var newRegisteredSpacesSetting = AddSpacesToRegisteredSpaces(allSpaces, registeredSpacesSetting.Value);
			var registeredWidgetsSetting = _settingRepository.SelectSettingById(_registeredAssetStudioWidgetsId);

			foreach (var site in sites)
			{
				var setting = _siteSettingRepository.SelectAll().FirstOrDefault(s => s.SiteId == site.ID && s.Name == registeredSpacesSetting.Name) ??
							  new SiteSetting { Name = registeredSpacesSetting.Name, SiteId = site.ID };
				setting.Value = newRegisteredSpacesSetting;
				if (setting.ID == Guid.Empty)
				{
					_siteSettingRepository.Insert(setting);
				}

				//Add the classification spaces to the registered widgets site setting.
				var registeredWidgetsSiteSetting = _siteSettingRepository.SelectAll().FirstOrDefault(s => s.SiteId == site.ID && s.Name == registeredWidgetsSetting.Name) ??
							  new SiteSetting { Name = registeredWidgetsSetting.Name, SiteId = site.ID };

				registeredWidgetsSiteSetting.Value = AddSpacesToRegisteredWidgets(allSpaces, string.IsNullOrEmpty(registeredWidgetsSiteSetting.Value) ? "<registeredWidgets></registeredWidgets>" : registeredWidgetsSiteSetting.Value); ;
				if(registeredWidgetsSetting.ID == Guid.Empty)
				{
					_siteSettingRepository.Insert(registeredWidgetsSiteSetting);
				}
			}
		}

		private string AddSpacesToRegisteredWidgets(IEnumerable<XElement> allSpaces, string settingValue)
		{
			XDocument document = XDocument.Parse(settingValue);

			foreach (XElement allSpace in allSpaces.Where(space => space.AdamAttribute("type").Value == _supportedType))
			{
				var newSettingElement = new XElement(allSpace);
				XAttribute descriptionAttribute = allSpace.AdamAttribute("isWidgetDescriptionVisible");
				XAttribute imageVisibleAttribute = allSpace.AdamAttribute("isWidgetImageVisible");
				XAttribute titleVisibleAttribute = allSpace.AdamAttribute("isWidgetTitleVisible");
				XAttribute cssClassAttribute = allSpace.AdamAttribute("widgetCssClass");
				XAttribute widgetStyleAttribute = allSpace.AdamAttribute("widgetStyle");

				newSettingElement.RemoveAttributes();
				//TODO: check duplicate name...?
				newSettingElement.SetAttributeValue("name", allSpace.AdamAttribute("name").Value);
				newSettingElement.SetAttributeValue("type", _classificationSpaceWidgetType);

				if (descriptionAttribute != null)
				{
					newSettingElement.SetAttributeValue("showDescription", descriptionAttribute.Value);
				}

				if (imageVisibleAttribute != null)
				{
					newSettingElement.SetAttributeValue("showImage", imageVisibleAttribute.Value);
				}

				if (titleVisibleAttribute != null)
				{
					newSettingElement.SetAttributeValue("showTitle", titleVisibleAttribute.Value);
				}

				if (cssClassAttribute != null)
				{
					string oldCssClass = cssClassAttribute.Value.Trim();
					//remove the -space from the end of the css class.
					string newCssClass = oldCssClass.Remove(oldCssClass.Length - 6, 6);
					string tileTemplate = string.Empty;

					switch (newCssClass)
					{
						case "background-image":
							tileTemplate = "BackgroundImage";
							break;
						case "small-image":
							tileTemplate = "SmallImage";
							break;
						case "text-only":
							tileTemplate = "TextOnly";
							break;
					}

					if (string.IsNullOrEmpty(tileTemplate))
					{
						newSettingElement.SetAttributeValue("cssClass", newCssClass);
					}
					else
					{
						newSettingElement.SetAttributeValue("tileTemplate", tileTemplate);
					}
				}
				else
				{
					newSettingElement.SetAttributeValue("tileTemplate", "TextOnly");
				}

				if (widgetStyleAttribute != null)
				{
					newSettingElement.SetAttributeValue("style", widgetStyleAttribute.Value);
				}

				document.Root.Add(newSettingElement);
			}

			return document.ToString(SaveOptions.None);
		}

		private string AddSpacesToRegisteredSpaces(IEnumerable<XElement> allSpaces, string settingValue)
		{
			var document = XDocument.Parse(settingValue);
			var newSettings = new List<XElement>();
			
			foreach (XElement allSpace in allSpaces)
			{
				var spaceType = allSpace.AdamAttribute("type").Value.ToLowerInvariant().Trim().Replace(" ", "");

				if (spaceType == _supportedType.ToLowerInvariant().Trim())
				{
					var newSettingElement = new XElement(allSpace);
					newSettingElement.Attributes()
						.Where(
							a =>
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "iswidgetdescriptionvisible" || 
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "iswidgettitlevisible" ||
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "widgetcssclass" ||
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "widgetlocation" ||
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "widgetsize" ||
								a.Name.LocalName.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() == "widgetstyle")
						.Remove();

					var imageVisibleAttribute = newSettingElement.AdamAttribute("isWidgetImageVisible");
					var identifierAttribute = newSettingElement.AdamAttribute("identifier");
					if (imageVisibleAttribute != null)
					{
						newSettingElement.SetAttributeValue("showImage", imageVisibleAttribute.Value);
						imageVisibleAttribute.Remove();
					}

					if (identifierAttribute != null)
					{
						newSettingElement.SetAttributeValue("classificationIdentifier", identifierAttribute.Value);
						identifierAttribute.Remove();
					}


					newSettings.Add(newSettingElement);
				}
				else
				{
					var comment = new XComment(allSpace.ToString(SaveOptions.None));
					document.Root.Add(comment);
				}

			}

			newSettings = FilterDoubles(newSettings, true);

			foreach (var newSetting in newSettings)
			{
				document.Root.Add(newSetting);
			}

			return document.ToString(SaveOptions.None);
		}

		private IEnumerable<XElement> SelectAllSpacesFromUserSetting(IEnumerable<IAdamSetting> userSettings)
		{
			var nodes = new List<XElement>();
			foreach (IAdamSetting adamSetting in userSettings)
			{
				nodes.AddRange(SelectAllSpacesFromSetting(adamSetting));
			}
			return nodes;
		}

		private IEnumerable<XElement> SelectAllSpacesFromSetting(IAdamSetting setting)
		{
			if (string.IsNullOrEmpty(setting.Value))
			{
				return new List<XElement>();
			}

			var spaces = XDocument.Parse(setting.Value).Descendants("add").ToList();

			//order all attribute alphabeticly for filtering out the doubles.
			foreach (var element in spaces)
			{
				var attrs = element.Attributes().ToList();
				attrs.Remove();
				attrs.Sort((a, b) => String.Compare(a.Name.LocalName, b.Name.LocalName, StringComparison.Ordinal));
				element.Add(attrs);
			}

			return spaces;
		}

		private List<XElement> FilterDoubles(IReadOnlyCollection<XElement> allSpaces, bool makeNameUnique)
		{
			//Filter out the doubles. I do this through string comparison, because this is to most easy to implement.
			var stringifiedList = new List<String>(allSpaces.Count);
			stringifiedList.AddRange(allSpaces.Select(xElement => xElement.ToString()));
			stringifiedList = stringifiedList.Distinct().ToList();

			//parse the string list back to XmlNodes and check if there are duplicate names left.
			//If so, we add a number to the name to make sure that the name is unique.
			var names = new List<string>();
			var nodeList = new List<XElement>();
			for (var index = 0; index < stringifiedList.Count; index++)
			{
				var xmlString = stringifiedList[index];
				var element = XElement.Parse(xmlString);

				if (makeNameUnique)
				{
					var name = element.Attribute(XName.Get("name")).Value;
					if (names.Contains(name))
					{
						element.SetAttributeValue("name", string.Format("{0}_{1}", name, index));
					}
					names.Add(name);
				}
				
				nodeList.Add(element);
			}

			return nodeList;
		}

		public void Update(SqlConnection sqlConnection)
		{
			var databaseContext = new DataContext(sqlConnection);
			_settingRepository = new SettingRepository<SystemSetting>(databaseContext);
			_siteSettingRepository = new SettingRepository<SiteSetting>(databaseContext);
			_userSettingRepository = new SettingRepository<UserSetting>(databaseContext);
			_userGroupSettingRepository = new SettingRepository<UserGroupSetting>(databaseContext);
			_siteRepository = new SiteRepository(databaseContext);

			UpdateSpaceSetting();

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

		internal class SettingRepository<T> where T : class, IAdamSetting
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
	}

	//TODO: uncomment when checking in.
	//public static class XElementExtensions
	//{
	//	public static XAttribute AdamAttribute(this XElement element, XName name)
	//	{
	//		var el = element.Attribute(name);
	//		if (el != null)
	//			return el;

	//		var elements = element.Attributes().Where(e => e.Name.LocalName.ToString().ToLowerInvariant() == name.ToString().ToLowerInvariant()).ToList();
	//		return !elements.Any() ? null : elements.First();
	//	}
	//}
}