(function () {
    "use strict";


    /**
     * @ngdoc directive
     * @name umbraco.directives.directive:umbMediaPicker3PropertyEditor
     * @function
     *
     * @description
     * The component for the Media Picker property editor.
     */
    angular
        .module("umbraco")
        .component("umbMediaPicker3PropertyEditor", {
            templateUrl: "views/propertyeditors/MediaPicker3/umb-media-picker3-property-editor.html",
            controller: MediaPicker3Controller,
            controllerAs: "vm",
            bindings: {
                model: "="
            },
            require: {
                propertyForm: "^form",
                umbProperty: "?^umbProperty",
                umbVariantContent: '?^^umbVariantContent',
                umbVariantContentEditors: '?^^umbVariantContentEditors',
                umbElementEditorContent: '?^^umbElementEditorContent'
            }
        });

    function MediaPicker3Controller($scope, editorService, clipboardService, localizationService, overlayService, userService, entityResource) {

        var unsubscribe = [];

        // Property actions:
        var copyAllMediasAction = null;
        var removeAllMediasAction = null;

        var vm = this;

        vm.loading = true;
        vm.currentMediaInFocus = null;
        vm.setMediaFocus = function (media) {
            if (vm.currentMediaInFocus !== null) {
                vm.currentMediaInFocus.focus = false;
            }
            vm.currentMediaInFocus = media;
            media.focus = true;
        };

        vm.supportCopy = clipboardService.isSupported();


        vm.labels = {};

        localizationService.localizeMany(["grid_addElement", "content_createEmpty", "mediaPicker_editMediaEntryLabel"]).then(function (data) {
            vm.labels.grid_addElement = data[0];
            vm.labels.content_createEmpty = data[1];
            vm.labels.mediaPicker_editMediaEntryLabel = data[2];
        });

        vm.$onInit = function() {

            vm.validationLimit = vm.model.config.validationLimit;
            vm.singleMode = vm.validationLimit.max === 1;
            vm.allowedTypes = vm.model.config.filter ? vm.model.config.filter.split(",") : null;

            copyAllMediasAction = {
                labelKey: "clipboard_labelForCopyAllEntries",
                labelTokens: [vm.model.label],
                icon: "documents",
                method: requestCopyAllMedias,
                isDisabled: true
            };

            removeAllMediasAction = {
                labelKey: 'clipboard_labelForRemoveAllEntries',
                labelTokens: [],
                icon: 'trash',
                method: requestRemoveAllMedia,
                isDisabled: true
            };

            var propertyActions = [];
            if(vm.supportCopy) {
                propertyActions.push(copyAllMediasAction);
            }
            propertyActions.push(removeAllMediasAction);

            if (vm.umbProperty) {
                vm.umbProperty.setPropertyActions(propertyActions);
            }

            if(vm.model.value === null || !Array.isArray(vm.model.value)) {
                vm.model.value = [];
            }

            vm.model.value.forEach(mediaEntry => updateMediaEntryData(mediaEntry));

            userService.getCurrentUser().then(function (userData) {

                if (!vm.model.config.startNodeId) {
                    if (vm.model.config.ignoreUserStartNodes === true) {
                        vm.model.config.startNodeId = -1;
                        vm.model.config.startNodeIsVirtual = true;
                    } else {
                        vm.model.config.startNodeId = userData.startMediaIds.length !== 1 ? -1 : userData.startMediaIds[0];
                        vm.model.config.startNodeIsVirtual = userData.startMediaIds.length !== 1;
                    }
                }

                // only allow users to add and edit media if they have access to the media section
                var hasAccessToMedia = userData.allowedSections.indexOf("media") !== -1;
                vm.allowEdit = hasAccessToMedia;
                vm.allowAdd = hasAccessToMedia;

                vm.loading = false;
            });

        };

        function setDirty() {
            if (vm.propertyForm) {
                vm.propertyForm.$setDirty();
            }
        }

        vm.addMediaAt = addMediaAt;
        function addMediaAt(createIndex, $event) {
            var mediaPicker = {
                startNodeId: vm.model.config.startNodeId,
                startNodeIsVirtual: vm.model.config.startNodeIsVirtual,
                dataTypeKey: vm.model.dataTypeKey,
                multiPicker: vm.singleMode !== true,
                clickPasteItem: function(item, mouseEvent) {
                    console.log("clickPasteItem", item, mouseEvent)
                    if (Array.isArray(item.data)) {
                        var indexIncrementor = 0;
                        item.data.forEach(function (entry) {
                            if (requestPasteFromClipboard(createIndex + indexIncrementor, entry, item.type)) {
                                indexIncrementor++;
                            }
                        });
                    } else {
                        requestPasteFromClipboard(createIndex, item.data, item.type);
                    }
                    if(!(mouseEvent.ctrlKey || mouseEvent.metaKey)) {
                        mediaPicker.close();
                    }
                },
                submit: function (model) {
                    editorService.close();

                    var indexIncrementor = 0;
                    model.selection.forEach((entry) => {
                        var mediaEntry = {};
                        mediaEntry.key = String.CreateGuid();
                        mediaEntry.mediaKey = entry.key;
                        updateMediaEntryData(mediaEntry);
                        vm.model.value.splice(createIndex + indexIncrementor, 0, mediaEntry);
                        indexIncrementor++;
                    });

                    setDirty();
                },
                close: function () {
                    editorService.close();
                }
            }

            if(vm.model.config.filter) {
                mediaPicker.filter = vm.model.config.filter;
            }

            mediaPicker.clickClearClipboard = function ($event) {
                clipboardService.clearEntriesOfType(clipboardService.TYPES.Media, vm.allowedTypes || null);
            };

            mediaPicker.clipboardItems = clipboardService.retriveEntriesOfType(clipboardService.TYPES.MEDIA, vm.allowedTypes || null);
            mediaPicker.clipboardItems.sort( (a, b) => {
                return b.date - a.date
            });

            editorService.mediaPicker(mediaPicker);
        }

        // To be used by infinite editor. (defined here cause we need configuration from property editor)
        function changeMediaFor(mediaEntry, onSuccess) {
            var mediaPicker = {
                startNodeId: vm.model.config.startNodeId,
                startNodeIsVirtual: vm.model.config.startNodeIsVirtual,
                dataTypeKey: vm.model.dataTypeKey,
                multiPicker: false,
                submit: function (model) {
                    editorService.close();

                    model.selection.forEach((entry) => {// only one.
                        mediaEntry.mediaKey = entry.key;
                    });

                    // reset focal and crops:
                    mediaEntry.crops = null;
                    mediaEntry.focalPoint = null;
                    updateMediaEntryData(mediaEntry);

                    if(onSuccess) {
                        onSuccess();
                    }
                },
                close: function () {
                    editorService.close();
                }
            }

            if(vm.model.config.filter) {
                mediaPicker.filter = vm.model.config.filter;
            }

            editorService.mediaPicker(mediaPicker);
        }

        function resetCrop(cropEntry) {
            Object.assign(cropEntry, vm.model.config.crops.find( c => c.alias === cropEntry.alias));
            cropEntry.coordinates = null;
            setDirty();
        }

        function updateMediaEntryData(mediaEntry) {

            mediaEntry.crops = mediaEntry.crops || [];
            mediaEntry.focalPoint = mediaEntry.focalPoint || {
                left: 0.5,
                top: 0.5
            };

            // Copy config and only transfer coordinates.
            var newCrops = Utilities.copy(vm.model.config.crops);
            newCrops.forEach(crop => {
                var oldCrop = mediaEntry.crops.filter(x => x.alias === crop.alias).shift();
                if (oldCrop && oldCrop.height === crop.height && oldCrop.width === crop.width) {
                    crop.coordinates = oldCrop.coordinates;
                }
            });
            mediaEntry.crops = newCrops;

        }

        vm.removeMedia = removeMedia;
        function removeMedia(media) {
            var index = vm.model.value.indexOf(media);
            if(index !== -1) {
                vm.model.value.splice(index, 1);
            }
        }
        function deleteAllMedias() {
            vm.model.value = [];
        }

        vm.activeMediaEntry = null;
        function setActiveMedia(mediaEntryOrNull) {
            vm.activeMediaEntry = mediaEntryOrNull;
        }

        vm.editMedia = editMedia;
        function editMedia(mediaEntry, options, $event) {

            if($event)
            $event.stopPropagation();

            options = options || {};

            setActiveMedia(mediaEntry);

            // make a clone to avoid editing model directly.
            var mediaEntryClone = Utilities.copy(mediaEntry);

            var mediaEditorModel = {
                $parentScope: $scope, // pass in a $parentScope, this maintains the scope inheritance in infinite editing
                $parentForm: vm.propertyForm, // pass in a $parentForm, this maintains the FormController hierarchy with the infinite editing view (if it contains a form)
                createFlow: options.createFlow === true,
                title: vm.labels.mediaPicker_editMediaEntryLabel,
                mediaEntry: mediaEntryClone,
                propertyEditor: {
                    changeMediaFor: changeMediaFor,
                    resetCrop: resetCrop
                },
                view: "views/common/infiniteeditors/mediaEntryEditor/mediaEntryEditor.html",
                size: "large",
                submit: function(model) {
                    vm.model.value[vm.model.value.indexOf(mediaEntry)] = mediaEntryClone;
                    setActiveMedia(null)
                    editorService.close();
                },
                close: function(model) {
                    if(model.createFlow === true) {
                        // This means that the user cancelled the creation and we should remove the media item.
                        // TODO: remove new media item.
                    }
                    setActiveMedia(null)
                    editorService.close();
                }
            };

            // open property settings editor
            editorService.open(mediaEditorModel);
        }

        var requestCopyAllMedias = function() {
            var mediaKeys = vm.model.value.map(x => x.mediaKey)
            entityResource.getByIds(mediaKeys, "Media").then(function (entities) {

                // gather aliases
                var aliases = entities.map(mediaEntity => mediaEntity.metaData.ContentTypeAlias)

                // remove duplicate aliases
                aliases = aliases.filter((item, index) => aliases.indexOf(item) === index);

                // get node name
                var contentNodeName = "?";
                var contentNodeIcon = null;
                if(vm.umbVariantContent) {
                    contentNodeName = vm.umbVariantContent.editor.content.name;
                    if(vm.umbVariantContentEditors) {
                        contentNodeIcon = vm.umbVariantContentEditors.content.icon.split(" ")[0];
                    } else if (vm.umbElementEditorContent) {
                        contentNodeIcon = vm.umbElementEditorContent.model.documentType.icon.split(" ")[0];
                    }
                } else if (vm.umbElementEditorContent) {
                    contentNodeName = vm.umbElementEditorContent.model.documentType.name;
                    contentNodeIcon = vm.umbElementEditorContent.model.documentType.icon.split(" ")[0];
                }

                localizationService.localize("clipboard_labelForArrayOfItemsFrom", [vm.model.label, contentNodeName]).then(function(localizedLabel) {
                    clipboardService.copyArray(clipboardService.TYPES.MEDIA, aliases, vm.model.value, localizedLabel, contentNodeIcon || "icon-thumbnail-list", vm.model.id);
                });
            });
        }

        vm.copyMedia = copyMedia;
        function copyMedia(mediaEntry) {
            entityResource.getById(mediaEntry.mediaKey, "Media").then(function (mediaEntity) {
                clipboardService.copy(clipboardService.TYPES.MEDIA, mediaEntity.metaData.ContentTypeAlias, mediaEntry, mediaEntity.name, mediaEntity.icon, mediaEntity.udi);
            });
        }
        function requestPasteFromClipboard(createIndex, pasteEntry, pasteType) {

            if (pasteEntry === undefined) {
                return false;
            }

            pasteEntry.key = String.CreateGuid();
            updateMediaEntryData(pasteEntry);
            vm.model.value.splice(createIndex, 0, pasteEntry);

            return true;

        }

        /*
        vm.requestRemoveMedia = requestRemoveMedia;
        function requestRemoveMedia(media) {
            localizationService.localizeMany(["general_delete", "mediaPicker_confirmRemoveMediaEntryMessage", "general_remove"]).then(function (data) {
                const overlay = {
                    title: data[0],
                    content: localizationService.tokenReplace(data[1], [media.name]),
                    submitButtonLabel: data[2],
                    close: function () {
                        overlayService.close();
                    },
                    submit: function () {
                        removeMedia(media);
                        overlayService.close();
                    }
                };

                overlayService.confirmDelete(overlay);
            });
        }
        */
        function requestRemoveAllMedia() {
            localizationService.localizeMany(["mediaPicker_confirmRemoveAllMediaEntryMessage", "general_remove"]).then(function (data) {
                overlayService.confirmDelete({
                    title: data[1],
                    content: data[0],
                    close: function () {
                        overlayService.close();
                    },
                    submit: function () {
                        deleteAllMedias();
                        overlayService.close();
                    }
                });
            });
        }


        vm.sortableOptions = {
            //containment: "parent",
            cursor: "grabbing",
            handle: "umb-media-card",
            cancel: "input,textarea,select,option",
            classes: ".umb-media-card--dragging",
            distance: 5,
            tolerance: "pointer",
            scroll: true,
            update: function (ev, ui) {
                setDirty();
            }
        };


        function onAmountOfMediaChanged() {

            // enable/disable property actions
            if (copyAllMediasAction) {
                copyAllMediasAction.isDisabled = vm.model.value.length === 0;
            }
            if (removeAllMediasAction) {
                removeAllMediasAction.isDisabled = vm.model.value.length === 0;
            }

            // validate limits:
            if (vm.propertyForm && vm.validationLimit) {

                var isMinRequirementGood = vm.validationLimit.min === null || vm.model.value.length >= vm.validationLimit.min;
                vm.propertyForm.minCount.$setValidity("minCount", isMinRequirementGood);

                var isMaxRequirementGood = vm.validationLimit.max === null || vm.model.value.length <= vm.validationLimit.max;
                vm.propertyForm.maxCount.$setValidity("maxCount", isMaxRequirementGood);
            }
        }

        unsubscribe.push($scope.$watch(() => vm.model.value.length, onAmountOfMediaChanged));

        $scope.$on("$destroy", function () {
            for (const subscription of unsubscribe) {
                subscription();
            }
        });
    }

})();