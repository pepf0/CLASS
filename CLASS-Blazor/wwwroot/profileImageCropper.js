window.profileImageCropper = {
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
                var x = (outputSize - width) / 2 + offsetX;
                var y = (outputSize - height) / 2 + offsetY;

                context.fillStyle = "#f1f5f9";
                context.fillRect(0, 0, outputSize, outputSize);
                context.drawImage(image, x, y, width, height);

                resolve(canvas.toDataURL("image/jpeg", 0.9));
            };

            image.onerror = function () {
                reject("Das Bild konnte nicht zugeschnitten werden.");
            };

            image.src = dataUrl;
        });
    }
};
