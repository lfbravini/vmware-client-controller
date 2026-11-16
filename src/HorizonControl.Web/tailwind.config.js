/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      fontFamily: {
        sans: ["IBM Plex Sans", "Segoe UI", "sans-serif"],
        display: ["IBM Plex Sans", "Segoe UI", "sans-serif"]
      },
      colors: {
        ink: "#0f172a",
        mist: "#e2e8f0",
        accent: "#0d9488"
      }
    }
  },
  plugins: []
};
