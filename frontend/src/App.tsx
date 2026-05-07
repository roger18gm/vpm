import { useState } from "react";
import "./App.css";
import { useJobs } from "./hooks/useJobs";

function App() {
  const { jobs, loading, error, createJob } = useJobs();
  const [showForm, setShowForm] = useState(false);
  const [formData, setFormData] = useState({
    title: "",
    description: "",
    status: "Scheduled",
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await createJob({ ...formData, dueDate: undefined });
    setFormData({ title: "", description: "", status: "Scheduled" });
    setShowForm(false);
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 to-indigo-100">
      <div className="container mx-auto px-4 py-8">
        <header className="mb-8">
          <h1 className="text-4xl font-bold text-gray-800 mb-2">VisionPaint</h1>
          <p className="text-lg text-gray-600">Job & Crew Management System</p>
        </header>

        <div className="bg-white rounded-lg shadow-lg p-6 mb-6">
          <div className="flex justify-between items-center mb-6">
            <h2 className="text-2xl font-semibold text-gray-800">Jobs</h2>
            <button
              onClick={() => setShowForm(!showForm)}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-bold py-2 px-4 rounded-lg transition-colors"
            >
              {showForm ? "Cancel" : "New Job"}
            </button>
          </div>

          {showForm && (
            <form
              onSubmit={handleSubmit}
              className="mb-6 p-4 bg-gray-50 rounded-lg"
            >
              <input
                type="text"
                placeholder="Job title"
                value={formData.title}
                onChange={(e) =>
                  setFormData({ ...formData, title: e.target.value })
                }
                className="w-full mb-3 p-2 border border-gray-300 rounded-lg"
                required
              />
              <textarea
                placeholder="Description"
                value={formData.description}
                onChange={(e) =>
                  setFormData({ ...formData, description: e.target.value })
                }
                className="w-full mb-3 p-2 border border-gray-300 rounded-lg"
                rows={3}
              />
              <select
                value={formData.status}
                onChange={(e) =>
                  setFormData({ ...formData, status: e.target.value })
                }
                className="w-full mb-3 p-2 border border-gray-300 rounded-lg"
              >
                <option>Scheduled</option>
                <option>In Progress</option>
                <option>Completed</option>
              </select>
              <button
                type="submit"
                className="bg-green-600 hover:bg-green-700 text-white font-bold py-2 px-4 rounded-lg transition-colors"
              >
                Create Job
              </button>
            </form>
          )}

          {error && <div className="text-red-600 mb-4">{error}</div>}

          {loading ? (
            <div className="text-center text-gray-600">Loading jobs...</div>
          ) : jobs.length === 0 ? (
            <div className="text-center text-gray-600">
              No jobs yet. Create one to get started!
            </div>
          ) : (
            <div className="space-y-4">
              {jobs.map((job) => (
                <div
                  key={job.id}
                  className="border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow"
                >
                  <h3 className="text-lg font-semibold text-gray-800">
                    {job.title}
                  </h3>
                  {job.description && (
                    <p className="text-gray-700">{job.description}</p>
                  )}
                  <div className="mt-2 flex items-center gap-4 text-sm text-gray-600">
                    <span className="inline-block px-2 py-1 bg-indigo-100 text-indigo-800 rounded">
                      {job.status}
                    </span>
                    <span>{new Date(job.createdAt).toLocaleDateString()}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default App;
