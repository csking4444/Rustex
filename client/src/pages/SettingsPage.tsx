import { Settings } from "lucide-react";
import { PlaceholderPage } from "@/components/ui/PlaceholderPage";

export default function SettingsPage() {
  return (
    <PlaceholderPage
      icon={Settings}
      title="Settings"
      description="Theme, notification channels, raid detection sensitivity, and API keys."
      phase="a future phase"
    />
  );
}
