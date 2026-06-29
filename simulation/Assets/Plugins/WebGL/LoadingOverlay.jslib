mergeInto(LibraryManager.library, {
  // Fades out the scene-load overlay defined in the WebGL template. No-op if the
  // template doesn't have it (e.g. a different template is selected).
  HideLoadingOverlay: function () {
    if (typeof document === "undefined") return;
    var overlay = document.getElementById("dd-overlay");
    if (overlay) overlay.classList.remove("show");
  },
});
