// 2025-12-22 - Camera capture JavaScript interop for mobile devices
window.cameraCapture = {
    stream: null,
    videoElement: null,
    canvasElement: null,

    startCamera: async function (useFrontCamera) {
        try {
            // Stop any existing stream
            this.stopCamera();

            this.videoElement = document.getElementById('cameraVideo');
            this.canvasElement = document.getElementById('cameraCanvas');

            if (!this.videoElement) {
                throw new Error('Video element not found');
            }

            // Request camera access
            const constraints = {
                video: {
                    facingMode: useFrontCamera ? 'user' : 'environment',
                    width: { ideal: 1920 },
                    height: { ideal: 1080 }
                },
                audio: false
            };

            this.stream = await navigator.mediaDevices.getUserMedia(constraints);
            this.videoElement.srcObject = this.stream;

            // Wait for video to be ready
            await new Promise((resolve) => {
                this.videoElement.onloadedmetadata = () => {
                    this.videoElement.play();
                    resolve();
                };
            });

            return true;
        } catch (error) {
            console.error('Camera access error:', error);
            throw error;
        }
    },

    stopCamera: function () {
        if (this.stream) {
            this.stream.getTracks().forEach(track => track.stop());
            this.stream = null;
        }
        if (this.videoElement) {
            this.videoElement.srcObject = null;
        }
    },

    capturePhoto: function () {
        if (!this.videoElement || !this.canvasElement) {
            throw new Error('Camera not initialized');
        }

        const video = this.videoElement;
        const canvas = this.canvasElement;

        // Set canvas size to video size
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;

        // Draw the current frame
        const ctx = canvas.getContext('2d');
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

        // Convert to base64 JPEG
        const dataUrl = canvas.toDataURL('image/jpeg', 0.9);

        return dataUrl;
    }
};
