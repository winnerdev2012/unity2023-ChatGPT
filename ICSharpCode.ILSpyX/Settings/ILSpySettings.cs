﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace ICSharpCode.ILSpyX.Settings
{
	/// <summary>
	/// Manages IL Spy settings.
	/// </summary>
	public class ILSpySettings : ISettingsProvider
	{
		/// <summary>
		/// This settings file path provider determines where to load settings file from, includes filename
		/// </summary>
		public static ISettingsFilePathProvider? SettingsFilePathProvider { get; set; }

		readonly XElement root;

		ILSpySettings()
		{
			this.root = new XElement("ILSpy");
		}

		ILSpySettings(XElement root)
		{
			this.root = root;
		}

		public XElement this[XName section] {
			get {
				return root.Element(section) ?? new XElement(section);
			}
		}

		/// <summary>
		/// Loads the settings file from disk.
		/// </summary>
		/// <returns>
		/// An instance used to access the loaded settings.
		/// </returns>
		public static ILSpySettings Load()
		{
			using (new MutexProtector(ConfigFileMutex))
			{
				try
				{
					XDocument doc = LoadWithoutCheckingCharacters(GetConfigFile());
					if (null == doc.Root)
					{
						return new ILSpySettings();
					}

					return new ILSpySettings(doc.Root);
				}
				catch (IOException)
				{
					return new ILSpySettings();
				}
				catch (XmlException)
				{
					return new ILSpySettings();
				}
			}
		}

		static XDocument LoadWithoutCheckingCharacters(string fileName)
		{
			return XDocument.Load(fileName, LoadOptions.None);
		}

		/// <summary>
		/// Saves a setting section.
		/// </summary>
		public static void SaveSettings(XElement section)
		{
			Update(
				delegate (XElement root) {
					XElement? existingElement = root.Element(section.Name);
					if (existingElement != null)
						existingElement.ReplaceWith(section);
					else
						root.Add(section);
				});
		}

		/// <summary>
		/// Updates the saved settings.
		/// We always reload the file on updates to ensure we aren't overwriting unrelated changes performed
		/// by another ILSpy instance.
		/// </summary>
		public static void Update(Action<XElement> action)
		{
			using (new MutexProtector(ConfigFileMutex))
			{
				string config = GetConfigFile();
				XDocument doc;
				try
				{
					doc = LoadWithoutCheckingCharacters(config);
				}
				catch (IOException)
				{
					// ensure the directory exists
					Directory.CreateDirectory(Path.GetDirectoryName(config)!);
					doc = new XDocument(new XElement("ILSpy"));
				}
				catch (XmlException)
				{
					doc = new XDocument(new XElement("ILSpy"));
				}
				doc.Root!.SetAttributeValue("version", DecompilerVersionInfo.Major + "." + DecompilerVersionInfo.Minor + "." + DecompilerVersionInfo.Build + "." + DecompilerVersionInfo.Revision);
				action(doc.Root);
				doc.Save(config, SaveOptions.None);
			}
		}

		void ISettingsProvider.Update(Action<XElement> action)
		{
			Update(action);
		}

		static string GetConfigFile()
		{
			if (null != SettingsFilePathProvider)
				return SettingsFilePathProvider.GetSettingsFilePath();

			throw new ArgumentNullException(nameof(SettingsFilePathProvider));
			// return "ILSpy.xml";
		}

		ISettingsProvider ISettingsProvider.Load()
		{
			return Load();
		}

		const string ConfigFileMutex = "01A91708-49D1-410D-B8EB-4DE2662B3971";

		/// <summary>
		/// Helper class for serializing access to the config file when multiple ILSpy instances are running.
		/// </summary>
		sealed class MutexProtector : IDisposable
		{
			readonly Mutex mutex;

			public MutexProtector(string name)
			{
				bool createdNew;
				this.mutex = new Mutex(true, name, out createdNew);
				if (!createdNew)
				{
					try
					{
						mutex.WaitOne();
					}
					catch (AbandonedMutexException)
					{
					}
				}
			}

			public void Dispose()
			{
				mutex.ReleaseMutex();
				mutex.Dispose();
			}
		}
	}
}
