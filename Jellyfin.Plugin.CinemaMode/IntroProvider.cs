using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CinemaMode
{
    public class IntroProvider : IIntroProvider
    {
        public string Name { get; } = "CinemaMode";

        public readonly ILogger<IntroProvider> Logger;

        // Add property to specify the target library name
        public string TargetLibraryName { get; set; } = "Movies"; // Default to "Movies"

        public IntroProvider(ILogger<IntroProvider> logger)
        {
            this.Logger = logger;
            this.Logger.LogInformation($"CinemaMode IntroProvider initialized with target library: '{TargetLibraryName}'");
        }

        public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
        {
            this.Logger.LogDebug($"GetIntros called for item: '{item.Name}' (ID: {item.Id}) by user: '{user.Username}'");

            // Check item type, for now just pre roll movies
            if (item is not MediaBrowser.Controller.Entities.Movies.Movie)
            {
                this.Logger.LogDebug($"Skipping intros for item '{item.Name}' - not a movie (type: {item.GetType().Name})");
                return Task.FromResult(Enumerable.Empty<IntroInfo>());
            }

            this.Logger.LogDebug($"Item '{item.Name}' is a movie, proceeding with library check");

            // Check if the item belongs to the target library
            if (!string.IsNullOrEmpty(TargetLibraryName))
            {
                this.Logger.LogDebug($"Library filtering enabled. Target library: '{TargetLibraryName}'");
                
                var library = GetLibraryFromItem(item);
                if (library == null)
                {
                    this.Logger.LogWarning($"Could not determine library for item '{item.Name}' (Path: {item.Path})");
                    return Task.FromResult(Enumerable.Empty<IntroInfo>());
                }

                this.Logger.LogDebug($"Item '{item.Name}' found in library: '{library.Name}'");

                if (!library.Name.Equals(TargetLibraryName, System.StringComparison.OrdinalIgnoreCase))
                {
                    this.Logger.LogInformation($"Skipping intros for item '{item.Name}' - in library '{library.Name}' but target is '{TargetLibraryName}'");
                    return Task.FromResult(Enumerable.Empty<IntroInfo>());
                }

                this.Logger.LogInformation($"Item '{item.Name}' matches target library '{TargetLibraryName}', proceeding with intros");
            }
            else
            {
                this.Logger.LogDebug("Library filtering disabled (TargetLibraryName is null or empty), proceeding with intros for all movies");
            }

            this.Logger.LogDebug($"Creating IntroManager and getting intros for item '{item.Name}'");
            IntroManager introManager = new IntroManager(this.Logger);
            var intros = introManager.Get(item, user);
            var introList = intros.ToList();
            
            this.Logger.LogInformation($"Found {introList.Count} intros for item '{item.Name}' in library '{TargetLibraryName}'");
            foreach (var intro in introList)
            {
                this.Logger.LogDebug($"Intro: ItemId={intro.ItemId}, Path={intro.Path}");
            }

            return Task.FromResult(introList.AsEnumerable());
        }

        private MediaBrowser.Controller.Entities.CollectionFolder GetLibraryFromItem(BaseItem item)
        {
            this.Logger.LogDebug($"Getting library for item: '{item.Name}' (Path: {item.Path})");
            
            try
            {
                // Get all libraries and find the one that contains this item
                var libraries = Plugin.LibraryManager.GetVirtualFolders();
                this.Logger.LogDebug($"Found {libraries.Count()} total libraries");

                foreach (var library in libraries)
                {
                    this.Logger.LogDebug($"Checking library: '{library.Name}' (ID: {library.ItemId}, Type: {library.CollectionType})");
                    
                    if (library.CollectionType.ToString().Equals("movies", System.StringComparison.OrdinalIgnoreCase))
                    {
                        this.Logger.LogDebug($"Library '{library.Name}' is a movie library, checking if item belongs to it");
                        
                        // Get the library folder
                        var libraryFolder = Plugin.LibraryManager.GetItemById(library.ItemId) as MediaBrowser.Controller.Entities.CollectionFolder;
                        if (libraryFolder == null)
                        {
                            this.Logger.LogWarning($"Could not get library folder for library '{library.Name}' (ID: {library.ItemId})");
                            continue;
                        }

                        // Check if the item belongs to this library
                        if (IsItemInLibrary(item, libraryFolder))
                        {
                            this.Logger.LogDebug($"Item '{item.Name}' belongs to library '{library.Name}'");
                            return libraryFolder;
                        }
                        else
                        {
                            this.Logger.LogDebug($"Item '{item.Name}' does not belong to library '{library.Name}'");
                        }
                    }
                    else
                    {
                        this.Logger.LogDebug($"Library '{library.Name}' is not a movie library (type: {library.CollectionType}), skipping");
                    }
                }

                this.Logger.LogWarning($"Could not find a movie library containing item '{item.Name}'");
            }
            catch (System.Exception ex)
            {
                this.Logger.LogError($"Error getting library for item {item.Name}: {ex.Message}");
                this.Logger.LogError($"Stack trace: {ex.StackTrace}");
            }
            return null;
        }

        private System.Guid GetLibraryIdFromItem(BaseItem item)
        {
            this.Logger.LogDebug($"GetLibraryIdFromItem called for item: '{item.Name}' (ID: {item.Id}, ParentId: {item.ParentId}, Path: {item.GetInternalMetadataPath()})");
            
            if (item.ParentId != System.Guid.Empty)
            {
                this.Logger.LogDebug($"Item '{item.Name}' has parent ID: {item.ParentId}, getting parent item");
                
                var parent = Plugin.LibraryManager.GetItemById(item.ParentId);
                if (parent != null)
                {
                    this.Logger.LogDebug($"Found parent: '{parent.Name}' (ID: {parent.Id}, Type: {parent.GetType().Name})");
                    
                    if (parent is MediaBrowser.Controller.Entities.CollectionFolder)
                    {
                        this.Logger.LogDebug($"Parent '{parent.Name}' is a CollectionFolder (library), returning its ID: {parent.Id}");
                        return parent.Id;
                    }
                    else
                    {
                        this.Logger.LogDebug($"Parent '{parent.Name}' is not a CollectionFolder, recursively checking its parent");
                        return GetLibraryIdFromItem(parent);
                    }
                }
                else
                {
                    this.Logger.LogWarning($"Could not find parent item with ID: {item.ParentId} for item '{item.Name}'");
                }
            }
            else
            {
                this.Logger.LogDebug($"Item '{item.Name}' has no parent (ParentId is Guid.Empty)");
            }
            
            this.Logger.LogDebug($"No library found for item '{item.Name}', returning Guid.Empty");
            return System.Guid.Empty;
        }

        private bool IsItemInLibrary(BaseItem item, BaseItem libraryFolder)
        {
            this.Logger.LogDebug($"Checking if item '{item.Name}' (ID: {item.Id}) is in library folder '{libraryFolder.Name}' (ID: {libraryFolder.Id})");
            
            try
            {
                // Use InternalItemsQuery to check if the item is in the specific library
                var query = new InternalItemsQuery();
                query.AncestorIds = new System.Guid[] { libraryFolder.Id };
                var libraryItems = Plugin.LibraryManager.GetItemList(query);
                
                bool isInLibrary = libraryItems.Any(libItem => libItem.Id == item.Id);
                this.Logger.LogDebug($"Item '{item.Name}' found in library '{libraryFolder.Name}': {isInLibrary} (Library contains {libraryItems.Count} items)");
                return isInLibrary;
            }
            catch (System.Exception ex)
            {
                this.Logger.LogError($"Error checking if item '{item.Name}' is in library '{libraryFolder.Name}': {ex.Message}");
                return false;
            }
        }

        public IEnumerable<string> GetAllIntroFiles()
        {
            this.Logger.LogDebug("GetAllIntroFiles called - not implemented");
            // not implemented
            return Enumerable.Empty<string>();
        }
    }
}
