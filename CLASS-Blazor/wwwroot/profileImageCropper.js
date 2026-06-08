window.profileImageCropper = {
    loadedImages: {},

    loadFromInput: function (inputId) {
        var input = document.getElementById(inputId);

        if (!input || !input.files || input.files.length === 0) {
            return Promise.reject("Es wurde kein Bild ausgewählt.");
        }

        var file = input.files[0];
        var previous = window.profileImageCropper.loadedImages[inputId];

        if (previous && previous.url) {
            URL.revokeObjectURL(previous.url);
        }

        var url = URL.createObjectURL(file);
        var state = {
            url: url,
            width: 0,
            height: 0
        };

        window.profileImageCropper.loadedImages[inputId] = state;

        return window.profileImageCropper.getImageDimensions(url).then(function (dimensions) {
            state.width = dimensions.width;
            state.height = dimensions.height;

            return {
                url: url,
                width: dimensions.width,
                height: dimensions.height
            };
        });
    },

    cropLoaded: function (inputId, zoom, offsetX, offsetY, outputSize) {
        var state = window.profileImageCropper.loadedImages[inputId];

        if (!state || !state.url) {
            return Promise.reject("Es wurde kein Bild geladen.");
        }

        return window.profileImageCropper.crop(state.url, zoom, offsetX, offsetY, outputSize);
    },

    getImageDimensions: function (dataUrl) {
        return new Promise(function (resolve, reject) {
            var image = new Image();

            image.onload = function () {
                resolve({
                    width: image.naturalWidth || image.width,
                    height: image.naturalHeight || image.height
                });
            };

            image.onerror = function () {
                reject("Die Bildmaße konnten nicht gelesen werden.");
            };

            image.src = dataUrl;
        });
    },

    crop: function (dataUrl, zoom, offsetX, offsetY, outputSize) {
        return new Promise(function (resolve, reject) {
            var image = new Image();

            image.onload = function () {
                var canvas = document.createElement("canvas");
                canvas.width = outputSize;
                canvas.height = outputSize;

                var context = canvas.getContext("2d");
                var scale = Math.max(outputSize / image.width, outputSize / image.height) * zoom;
                var width = image.width * scale;
                var height = image.height * scale;
                var maxOffsetX = Math.max(0, (width - outputSize) / 2);
                var maxOffsetY = Math.max(0, (height - outputSize) / 2);
                var clampedOffsetX = Math.min(Math.max(offsetX, -maxOffsetX), maxOffsetX);
                var clampedOffsetY = Math.min(Math.max(offsetY, -maxOffsetY), maxOffsetY);
                var x = (outputSize - width) / 2 + clampedOffsetX;
                var y = (outputSize - height) / 2 + clampedOffsetY;

                context.fillStyle = "#f1f5f9";
                context.fillRect(0, 0, outputSize, outputSize);
                context.drawImage(image, x, y, width, height);

                resolve(canvas.toDataURL("image/webp", 0.9));
            };

            image.onerror = function () {
                reject("Das Bild konnte nicht zugeschnitten werden.");
            };

            image.src = dataUrl;
        });
    }
};
