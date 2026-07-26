import { CalendarClock } from "lucide-react";
import { PlaceholderPage } from "@/components/ui/PlaceholderPage";

export default function EventsPage() {
  return (
    <PlaceholderPage
      icon={CalendarClock}
      title="Events"
      description="Cargo ship, patrol heli, Bradley, crates, and server restart tracking."
      phase="Phase 3"
    />
  );
}
