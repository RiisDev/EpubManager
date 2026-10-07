using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EpubManager.ContentSources;

namespace EpubManager
{
	/// <summary>
	/// Entry point for EpubManager.
	/// </summary>
	public static class EpubManager
	{
		/// <summary>
		/// Registry of site writers. Writers ship as separate <c>EpubManager.Writers.*</c> packages;
		/// install one and it is discovered automatically (loaded assemblies, plus
		/// <c>EpubManager.Writers.*.dll</c> next to the application).
		/// </summary>
		public static class Writers
		{
			private static readonly object Gate = new();
			private static Dictionary<string, IStoryWriter>? _writers;

			/// <summary>The Literotica writer, or <see langword="null"/> if EpubManager.Writers.Literotica isn't installed.</summary>
			public static IStoryWriter? Literotica => Get("Literotica");

			/// <summary>The ScribbleHub writer, or <see langword="null"/> if its package isn't installed.</summary>
			public static IStoryWriter? ScribbleHub => Get("ScribbleHub");

			/// <summary>Names of all discovered or registered writers.</summary>
			public static IReadOnlyCollection<string> Available
			{
				get { lock (Gate) return Load().Keys.ToArray(); }
			}

			/// <summary>Gets a writer by name (case-insensitive), or <see langword="null"/> if not installed.</summary>
			public static IStoryWriter? Get(string name)
			{
				lock (Gate) return Load().TryGetValue(name, out IStoryWriter? w) ? w : null;
			}

			/// <summary>Manually registers (or replaces) a writer, for custom or non-discoverable plugins.</summary>
			public static void Register(string name, IStoryWriter writer)
			{
				lock (Gate) Load()[name] = writer;
			}

			private static Dictionary<string, IStoryWriter> Load()
			{
				if (_writers != null) return _writers;
				_writers = new(StringComparer.OrdinalIgnoreCase);

				const string prefix = "EpubManager.Writers.";
				HashSet<string> loaded = new(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? ""));

				// Referenced-but-untouched plugins (incl. single-file bundles) aren't loaded yet.
				foreach (AssemblyName r in Assembly.GetEntryAssembly()?.GetReferencedAssemblies() ?? Array.Empty<AssemblyName>())
				{
					if (r.Name == null || !r.Name.StartsWith(prefix, StringComparison.Ordinal) || loaded.Contains(r.Name)) continue;
					try { Assembly.Load(r); loaded.Add(r.Name); } catch { /* skip */ }
				}

				foreach (string dll in Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, prefix + "*.dll"))
				{
					if (loaded.Contains(Path.GetFileNameWithoutExtension(dll))) continue;
					try { Assembly.LoadFrom(dll); } catch { /* not a loadable plugin, skip */ }
				}

				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()
					.Where(a => a.GetName().Name?.StartsWith(prefix, StringComparison.Ordinal) == true))
				{
					Type[] types;
					try { types = asm.GetTypes(); }
					catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }

					foreach (Type t in types.Where(t => t.IsClass && !t.IsAbstract && typeof(IStoryWriter).IsAssignableFrom(t)
						&& t.GetConstructor(Type.EmptyTypes) != null))
					{
						try { _writers[t.Name] = (IStoryWriter)Activator.CreateInstance(t)!; } catch { /* skip broken plugin */ }
					}
				}

				return _writers;
			}
		}
	}
}
