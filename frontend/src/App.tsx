import { useState } from "react";
import "./App.css";

function App() {
  const [count, setCount] = useState(0);

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 to-indigo-100">
      <div className="container mx-auto px-4 py-8">
        <header className="mb-8">
          <h1 className="text-4xl font-bold text-gray-800 mb-2">VisionPaint</h1>
          <p className="text-lg text-gray-600">Job & Crew Management System</p>
        </header>

        <div className="bg-white rounded-lg shadow-lg p-6 mb-6">
          <h2 className="text-2xl font-semibold text-gray-800 mb-4">Welcome</h2>
          <p className="text-gray-700 mb-4">
            This is the VisionPaint frontend skeleton. The backend is running at
            http://localhost:5000
          </p>

          <div className="mb-6">
            <button
              onClick={() => setCount((count) => count + 1)}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-bold py-2 px-4 rounded-lg transition-colors"
            >
              count is {count}
            </button>
          </div>

          <div className="bg-gray-50 rounded p-4 text-sm text-gray-700">
            <p>
              <strong>Next Steps:</strong>
            </p>
            <ul className="list-disc ml-5 mt-2">
              <li>Configure Supabase database connection</li>
              <li>Integrate with backend API endpoints</li>
              <li>Build job management features</li>
              <li>Implement time tracking</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

export default App;
