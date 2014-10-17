using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Adam.Core.DatabaseManagerLibrary;
using Adam.Tools.LogHandler;

namespace Adam.Core.DatabaseManager
{
	public class Upgrader : UpgradeScriptBase
	{
		private readonly Guid _assetStudioWidgetsId = new Guid("C5DB4DFD-CCDD-436E-82F3-CA91A456C428");
		private readonly Guid _configStudioWidgetsId = new Guid("8ADAC44E-FC07-4A98-B6A5-DBEE153F7F5F");
		private readonly Guid _studioSelectorWidgetsGuid = new Guid("66D7173E-1439-4DBC-AB99-610589EBF3E9");

		private readonly Guid _registeredAssetStudioWidgetsId = new Guid("6CA90AAE-2A38-4DDD-B4AA-673945835348");
		private readonly Guid _registeredConfigStudioWidgetsId = new Guid("12735B9D-FDFE-425A-A6FB-FEE065CCA32A");
		private readonly Guid _registeredStudioSelecterWidgetsId = new Guid("00261546-F7CB-4532-891E-D9045B4CCDFC");

		private SettingRepository<SystemSetting> _settingRepository;
		private SettingRepository<SiteSetting> _siteSettingRepository;
		private SettingRepository<UserSetting> _userSettingRepository;
		private SettingRepository<UserGroupSetting> _userGroupSettingRepository;
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

				UpdateWidgetSettingForStudio(_assetStudioWidgetsId, _registeredAssetStudioWidgetsId);
				UpdateWidgetSettingForStudio(_configStudioWidgetsId, _registeredConfigStudioWidgetsId);
				UpdateWidgetSettingForStudio(_studioSelectorWidgetsGuid, _registeredStudioSelecterWidgetsId);

				databaseContext.SubmitChanges();
			}
			catch (Exception exception)
			{
				LogManager.Write(LogSeverity.Error, exception);
			}
		}

		public void Update(SqlConnection sqlConnection)
		{
			var databaseContext = new DataContext(sqlConnection);
			_settingRepository = new SettingRepository<SystemSetting>(databaseContext);
			_siteSettingRepository = new SettingRepository<SiteSetting>(databaseContext);
			_userSettingRepository = new SettingRepository<UserSetting>(databaseContext);
			_userGroupSettingRepository = new SettingRepository<UserGroupSetting>(databaseContext);
			_siteRepository = new SiteRepository(databaseContext);

			UpdateWidgetSettingForStudio(_assetStudioWidgetsId, _registeredAssetStudioWidgetsId);
			UpdateWidgetSettingForStudio(_configStudioWidgetsId, _registeredConfigStudioWidgetsId);
			UpdateWidgetSettingForStudio(_studioSelectorWidgetsGuid, _registeredStudioSelecterWidgetsId);

			databaseContext.SubmitChanges();
		}

		public void UpdateWidgetSettingForStudio(Guid widgetId, Guid registerdWidgetId)
		{
			var systemSetting = _settingRepository.SelectSettingById(widgetId);
			var userSettings = _userSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();
			var siteSettings = _siteSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();
			var userGroupSettings = _userGroupSettingRepository.SelectSettingsByName(systemSetting.Name).ToList();
			var sites = _siteRepository.SelectAll();

			var valueToUse = systemSetting.Value != systemSetting.DefaultValue ? systemSetting.Value : systemSetting.DefaultValue;
			if (string.IsNullOrEmpty(valueToUse))
			{
				valueToUse = systemSetting.DefaultValue;
			}

			//get all the widgets from the system, site, user and usergroup setting.
			var systemSettingWidgets = SelectAllWidgetsFromSetting(valueToUse);
			var siteSettingWidgets = SelectAllWidgetsFromUserSetting(siteSettings);
			var userSettingWidgets = SelectAllWidgetsFromUserSetting(userSettings);
			var userGroupSettingWidgets = SelectAllWidgetsFromUserSetting(userGroupSettings);

			var allWidgets = new List<XmlNode>(systemSettingWidgets);
			allWidgets.AddRange(siteSettingWidgets);
			allWidgets.AddRange(userSettingWidgets);
			allWidgets.AddRange(userGroupSettingWidgets);

			allWidgets = FilterDoubles(allWidgets);

			//create and save the new setting
			var newRegisteredWidgetSettingXml = CreateRegisteredWidgetSettingXml(allWidgets);
			string homePageWidgetsXmlValue = CreateNewWidgetXmlValue(newRegisteredWidgetSettingXml, valueToUse);
			//comment out the old system setting value (no longer needed)
			if(!string.IsNullOrEmpty(systemSetting.Value))
			{
				systemSetting.Value = CommentOldValue(systemSetting.Value);
			}
			
			foreach (var userSetting in userSettings)
			{
				userSetting.Value = CreateNewWidgetXmlValue(newRegisteredWidgetSettingXml, userSetting.Value);
			}

			foreach (var siteSetting in siteSettings)
			{
				siteSetting.Value = CreateNewWidgetXmlValue(newRegisteredWidgetSettingXml, siteSetting.Value);
			}

			foreach (var userGroupSetting in userGroupSettings)
			{
				userGroupSetting.Value = CreateNewWidgetXmlValue(newRegisteredWidgetSettingXml, userGroupSetting.Value);

			}

			var result = RemoveOldNameNode(newRegisteredWidgetSettingXml);
			var registeredWidgetsSetting = _settingRepository.SelectSettingById(registerdWidgetId);


			foreach (var site in sites)
			{
				var registeredWidgetSiteSetting = _siteSettingRepository.SelectAll().FirstOrDefault(s => s.SiteId == site.ID && s.Name == registeredWidgetsSetting.Name) ??
							  new SiteSetting { Name = registeredWidgetsSetting.Name, SiteId = site.ID };

				var homePageWidgets = _siteSettingRepository.SelectAll().FirstOrDefault(s => s.SiteId == site.ID && s.Name == systemSetting.Name) ??
							  new SiteSetting { Name = systemSetting.Name, SiteId = site.ID };

				registeredWidgetSiteSetting.Value = result;
				homePageWidgets.Value = homePageWidgetsXmlValue;

				if (registeredWidgetSiteSetting.ID == Guid.Empty)
				{
					_siteSettingRepository.Insert(registeredWidgetSiteSetting);
				}

				if (homePageWidgets.ID == Guid.Empty)
				{
					_siteSettingRepository.Insert(homePageWidgets);
				}
			}
		}

		private string CommentOldValue(string value)
		{
			var document = XDocument.Parse(value);
			var allElements = document.Elements("add").ToList();
			var builder = new StringBuilder();
			foreach (var allElement in allElements)
			{
				builder.AppendLine(allElement.ToString());
			}
			var comment = new XComment(builder.ToString());

			if (document.Root != null)
			{
				document.Root.RemoveAll();
				document.Root.Add(comment);
			}

			return document.ToString(SaveOptions.None);
		}

		private IEnumerable<XmlNode> SelectAllWidgetsFromUserSetting(IEnumerable<IAdamSetting> userSettings)
		{
			var nodes = new List<XmlNode>();
			foreach (var adamSetting in userSettings)
			{
				nodes.AddRange(SelectAllWidgetsFromSetting(adamSetting.Value));
			}

			return nodes;
		}

		private IEnumerable<XmlNode> SelectAllWidgetsFromSetting(string settting)
		{
			//<widgets priority="100" tileWidth="110" tileHeight="90" gutterHeight="60" gutterWidth="80">
			//	<add name="LatestRecords" type="Adam.Web.Studio.Providers.Widgets.RecordsWidget, Adam.Web.Studio" location="0, 0" size="2, 3" MaxItems="10" SearchExpression="filecount >= 1" SortOrder="createdon desc"/>
			//</widgets>
			var nodes = new List<XmlNode>();
			if (string.IsNullOrEmpty(settting))
			{
					return nodes;
			}

			var xmlsetting = new XmlDocument();
			xmlsetting.LoadXml(settting);

			var widgets = xmlsetting.SelectNodes("/widgets/add");

			if (widgets != null)
			{
				nodes = widgets.Cast<XmlNode>().ToList();
				for (int index = 0; index < nodes.Count; index++)
				{
					var xmlNode = nodes[index];
					var nameAttribute = xmlNode.Attributes.GetNamedItem("name");
					//there is a possibility that the name is empty, if so we fill out a random name because in the new structure name is required.
					if (nameAttribute == null)
					{
						var attribute = xmlsetting.CreateAttribute("name");
						attribute.Value = string.Format("Widget{0}", index);
						xmlNode.Attributes.Append(attribute);
					}
				}
			}
			return nodes;
		}

		private List<XmlNode> FilterDoubles(IReadOnlyCollection<XmlNode> allWidgets)
		{
			var nodeList = new List<XmlNode>();
			var names = new List<string>();

			//Filter out the doubles. I do this through string comparison, because this is to most easy to implement.
			var stringifiedList = new List<String>(allWidgets.Count);
			stringifiedList.AddRange(allWidgets.Select(ParseXmlToString));
			stringifiedList = stringifiedList.Distinct().ToList();

			//parse the string list back to XmlNodes and check if there are duplicate names left.
			//If so, we add a number to the name to make sure that the name is unique.
			for (var index = 0; index < stringifiedList.Count; index++)
			{
				var xmlString = stringifiedList[index];
				var element = XElement.Parse(xmlString);

				var name = element.Attribute(XName.Get("name")).Value;
				if (names.Contains(name))
				{
					element.SetAttributeValue("name", string.Format("{0}_{1}", name, index));
					element.SetAttributeValue("oldName", name);
				}
				names.Add(name);
				nodeList.Add(GetXmlNode(element));
			}

			return nodeList;
		}

		private string CreateRegisteredWidgetSettingXml(IList<XmlNode> widgets)
		{
			//<registeredwidgets>
			//  <add name="LatestRecords" type="Adam.Web.Studio.Providers.Widgets.RecordsWidget, Adam.Web.Studio" MaxItems="10" SearchExpression="filecount >= 1" SortOrder="createdon desc" />
			//</registeredwidgets>
			var xmlDocument = new XmlDocument();
			xmlDocument.LoadXml("<registeredwidgets></registeredwidgets>");
			var root = xmlDocument.DocumentElement;

			if (root != null)
			{
				for (int index = 0; index < widgets.Count; index++)
				{
					var widget = widgets[index];
					var newNode = xmlDocument.ImportNode(widget, true);
					var attributes = newNode.Attributes;
					if (attributes != null)
					{
						attributes.Remove(attributes["location"]);
						attributes.Remove(attributes["size"]);
					}
					root.AppendChild(newNode);
				}
			}

			return ParseXmlToString(xmlDocument);
		}

		private string CreateNewWidgetXmlValue(string registeredWidgets, string oldValue)
		{
			//<widgets priority="100" tileWidth="96" tileHeight="147" gutterWidth="53" gutterHeight="40" version="2" >
			//  <add name="LatestRecords" location="0, 0" />
			//</widgets>
			IEnumerable<XElement> newXml = XDocument.Parse(oldValue).Root.Elements("add");
			var registeredWidgetsXml = XDocument.Parse(registeredWidgets).Root.Elements("add").ToList();
			foreach (XElement element in newXml)
			{
				//if no name is found in the old setting, compare all the properties and fill out the new name.
				if (element.Attribute("name") == null)
				{
					foreach (var renamedWidget in registeredWidgetsXml)
					{
						var attributes = renamedWidget.Attributes().Where(a => a.Name != "oldName" && a.Name != "name").ToList();
						var oldAttriubutes = element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").ToList();

						if (attributes.SequenceEqual(oldAttriubutes, new AttributesComparer()))
						{
							element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").Remove();
							element.SetAttributeValue("name", renamedWidget.Attribute("name").Value);
							element.Elements().Remove();

						}
					}
				}
				else
				{
					//try to find the widget in the registered settings xml.
					var registeredWidget = registeredWidgetsXml.SingleOrDefault(r => r.Attribute("name").Value == element.Attribute("name").Value);
					if (registeredWidget != null)
					{
						//get the attributes from the old setting and the new registered widget setting.
						var attributes = registeredWidget.Attributes().Where(a => a.Name != "oldName" && a.Name != "name").ToList();
						var oldAttriubutes = element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").ToList();

						//if the collections are the same, we have a match. Now we can remove all the unneccessary attributes.
						if (attributes.SequenceEqual(oldAttriubutes, new AttributesComparer()))
						{
							element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").Remove();
							element.Elements().Remove();
						}
						else
						{
							//if nothing is found with the given name, then we have to look to the old name and do the comparison again.
							var renamedWidgets = registeredWidgetsXml.Where(r => r.Attribute("oldName") != null && r.Attribute("oldName").Value == element.Attribute("name").Value).ToList();
							foreach (var renamedWidget in renamedWidgets)
							{
								attributes = renamedWidget.Attributes().Where(a => a.Name != "oldName" && a.Name != "name").ToList();
								oldAttriubutes = element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").ToList();

								if (attributes.SequenceEqual(oldAttriubutes, new AttributesComparer()))
								{
									element.Attributes().Where(a => a.Name != "location" && a.Name != "size" && a.Name != "name").Remove();
									element.Attribute("name").Value = renamedWidget.Attribute("name").Value;
									element.Elements().Remove();
								}
							}
						}
					}
				}

			}

			var xmlString = new StringBuilder();
			foreach (var xElement in newXml)
			{
				xmlString.Append(xElement);
			}

			var xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(oldValue);
			xmlDoc.DocumentElement.InnerXml = xmlString.ToString();

			return ParseXmlToString(xmlDoc);
		}

		private static string RemoveOldNameNode(string newRegisteredWidgetSettingXml)
		{
			var xdoc = XDocument.Parse(newRegisteredWidgetSettingXml);
			foreach (var node in xdoc.Descendants().Where(e => e.Attribute("oldName") != null))
			{
				node.Attribute("oldName").Remove();
			}

			return xdoc.ToString();
		}

		private XmlNode GetXmlNode(XElement element)
		{
			using (XmlReader xmlReader = element.CreateReader())
			{
				XmlDocument xmlDoc = new XmlDocument();
				xmlDoc.Load(xmlReader);
				return xmlDoc.DocumentElement;
			}
		}

		private string ParseXmlToString(XmlNode xmlDocument)
		{
			using (var stringWriter = new StringWriter())
			{
				using (var xmlTextWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment }))
				{
					xmlDocument.WriteTo(xmlTextWriter);
					xmlTextWriter.Flush();
					return stringWriter.GetStringBuilder().ToString();
				}
			}
		}

		internal class AttributesComparer : IEqualityComparer<XAttribute>
		{
			public bool Equals(XAttribute x, XAttribute y)
			{
				return x.Value == y.Value && x.Name == y.Name;
			}

			public int GetHashCode(XAttribute obj)
			{
				return obj.GetHashCode();
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

		internal interface IAdamSetting
		{
			Guid ID { get; set; }
			string Name { get; set; }
			string Value { get; set; }
		}

		[Table(Name = "tblSETTINGS")]
		internal class SystemSetting : IAdamSetting
		{
			[Column(IsPrimaryKey = true)]
			public Guid ID { get; set; }

			[Column]
			public string Name { get; set; }

			[Column]
			public string Kind { get; set; }

			[Column]
			public string Value { get; set; }

			[Column]
			public string DefaultValue { get; set; }
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
}