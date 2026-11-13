using LocalServicesMarketplace.Models;

namespace LocalServicesMarketplace.Data.Seed;

/// <summary>Static demo content. All people below are fictional.</summary>
public static class SeedData
{
    public const string DemoDomain = "demo.lsm.local";

    public static readonly (string Name, string Slug, string Icon, string Description, int Order)[] Categories =
    [
        ("Plumbing", "plumbing", "bi-droplet", "Leaks, taps, drains, bathroom fittings and water tanks.", 1),
        ("Electrical", "electrical", "bi-lightning-charge", "Wiring, switchboards, fans, lights and inverter set-up.", 2),
        ("AC Repair", "ac-repair", "bi-snow", "Servicing, gas refills, installation and repair for split and window ACs.", 3),
        ("Home Cleaning", "home-cleaning", "bi-stars", "Deep cleaning for kitchens, bathrooms and whole homes.", 4),
        ("Carpentry", "carpentry", "bi-hammer", "Furniture repair, locks, hinges and custom woodwork.", 5),
        ("Painting", "painting", "bi-paint-bucket", "Interior and exterior painting, polish and touch-ups.", 6),
        ("Tutoring", "tutoring", "bi-book", "Home and online tutors for school subjects and languages.", 7),
        ("Beauty and Salon", "beauty-salon", "bi-scissors", "Haircuts, facials and makeup in the comfort of home.", 8),
        ("Appliance Repair", "appliance-repair", "bi-tools", "Washing machines, refrigerators, microwaves and more.", 9),
        ("Pest Control", "pest-control", "bi-bug", "Cockroach, termite, bed bug and general pest treatment.", 10)
    ];

    public record DemoService(string Title, decimal Price, PriceUnit Unit, int Minutes, string Description);

    public static readonly Dictionary<string, DemoService[]> ServicesByCategory = new()
    {
        ["plumbing"] =
        [
            new("Tap and mixer installation", 249, PriceUnit.Fixed, 45, "Install or replace a tap, mixer or shower head. Fittings supplied by you or bought at cost."),
            new("Leak detection and repair", 399, PriceUnit.PerVisit, 60, "Find and fix leaks in pipes, joints and cisterns. Materials billed separately."),
            new("Drain unclogging", 349, PriceUnit.Fixed, 60, "Clear blocked kitchen sinks, wash basins and floor drains.")
        ],
        ["electrical"] =
        [
            new("Ceiling fan installation", 199, PriceUnit.Fixed, 45, "Mount and connect a ceiling fan, including regulator wiring."),
            new("Switchboard and socket repair", 149, PriceUnit.PerVisit, 30, "Repair or replace switches, sockets and loose connections."),
            new("Inverter installation", 699, PriceUnit.Fixed, 90, "Install an inverter and battery with safe changeover wiring.")
        ],
        ["ac-repair"] =
        [
            new("AC general service", 549, PriceUnit.Fixed, 60, "Filter and coil cleaning with a cooling performance check."),
            new("Gas refill (split AC)", 2499, PriceUnit.Fixed, 90, "Leak check and full refrigerant top-up for split ACs."),
            new("AC installation", 1499, PriceUnit.Fixed, 120, "Install a split AC with up to 3 m of copper piping.")
        ],
        ["home-cleaning"] =
        [
            new("Bathroom deep cleaning", 499, PriceUnit.Fixed, 90, "Tiles, fittings, mirrors and floor scrubbed and descaled."),
            new("Kitchen deep cleaning", 1199, PriceUnit.Fixed, 180, "Degreasing of slab, chimney exterior, cabinets and sink."),
            new("Full home deep cleaning (2BHK)", 3999, PriceUnit.Fixed, 360, "Every room, kitchen and two bathrooms. Team of two.")
        ],
        ["carpentry"] =
        [
            new("Furniture repair", 349, PriceUnit.PerHour, 60, "Fix wobbly tables, broken drawers and loose joints."),
            new("Door lock installation", 299, PriceUnit.Fixed, 45, "Fit a new mortise or cylindrical lock on wooden doors."),
            new("Wardrobe hinge and channel fix", 399, PriceUnit.Fixed, 60, "Replace hinges, drawer channels and handles.")
        ],
        ["painting"] =
        [
            new("Accent wall painting", 1499, PriceUnit.Fixed, 240, "One wall, two coats, with surface preparation."),
            new("Room painting (up to 120 sq ft)", 4999, PriceUnit.Fixed, 480, "Walls and ceiling with premium emulsion. Furniture covered."),
            new("Wood polish and touch-ups", 899, PriceUnit.PerHour, 120, "Polish doors and furniture, fix chips and scratches.")
        ],
        ["tutoring"] =
        [
            new("Maths tuition (class 9-10)", 600, PriceUnit.PerHour, 60, "CBSE / ICSE syllabus with weekly practice worksheets."),
            new("Physics doubt session", 500, PriceUnit.PerHour, 60, "One-to-one session to clear concepts and exam questions."),
            new("Spoken English session", 450, PriceUnit.PerHour, 60, "Conversation practice, pronunciation and confidence building.")
        ],
        ["beauty-salon"] =
        [
            new("Haircut and styling at home", 499, PriceUnit.Fixed, 60, "Wash, cut and blow-dry with salon-grade products."),
            new("Fruit facial", 799, PriceUnit.Fixed, 60, "Cleansing, scrub, massage and mask for a fresh glow."),
            new("Party makeup", 2499, PriceUnit.Fixed, 90, "Makeup and simple hairstyling for an event.")
        ],
        ["appliance-repair"] =
        [
            new("Washing machine repair", 449, PriceUnit.PerVisit, 60, "Diagnosis and repair for front and top loaders. Parts extra."),
            new("Refrigerator repair", 499, PriceUnit.PerVisit, 60, "Cooling, thermostat and compressor issues diagnosed on site."),
            new("Microwave repair", 349, PriceUnit.PerVisit, 45, "Heating, turntable and panel faults.")
        ],
        ["pest-control"] =
        [
            new("Cockroach and ant treatment (2BHK)", 1199, PriceUnit.Fixed, 90, "Odourless gel treatment for kitchen and bathrooms."),
            new("Termite treatment", 2999, PriceUnit.Fixed, 180, "Drill-fill-seal treatment with a 1-year warranty."),
            new("Bed bug treatment", 1599, PriceUnit.Fixed, 120, "Two-visit spray treatment for beds, sofas and curtains.")
        ]
    };

    public record DemoProvider(
        string FullName, string Email, string Phone, string Category, string City, int Experience,
        string Headline, string Skills, string Areas, VerificationStatus Status, int StartHour, int EndHour, string Bio);

    public static readonly DemoProvider[] Providers =
    [
        new("Rohit Verma", "rohit.verma", "9810000001", "electrical", "New Delhi", 9, "Licensed electrician for homes and small offices",
            "House wiring, MCB and DB panels, Fan and light fitting, Inverter set-up", "Lajpat Nagar, Saket, Hauz Khas, Malviya Nagar",
            VerificationStatus.Approved, 9, 18,
            "I have worked as a licensed electrician in South Delhi for nine years. I carry my own tools and test equipment, explain the fault before I start, and give a written estimate for anything above the visiting charge."),
        new("Suresh Yadav", "suresh.yadav", "9810000002", "plumbing", "New Delhi", 12, "Leak fixes, fittings and bathroom plumbing",
            "Leak repair, Tap and mixer fitting, Drain unclogging, Water tank cleaning", "Karol Bagh, Rajouri Garden, Patel Nagar",
            VerificationStatus.Approved, 9, 18,
            "Twelve years of residential plumbing across West Delhi. Most small jobs are finished in a single visit and I always leave the bathroom clean."),
        new("Mohammed Arif", "mohammed.arif", "9810000003", "ac-repair", "Noida", 7, "Split and window AC service, gas refill and installation",
            "Split AC service, Gas charging, Installation, PCB repair", "Sector 18, Sector 62, Sector 137, Greater Noida West",
            VerificationStatus.Approved, 9, 19,
            "Trained with a leading AC brand's service centre before going independent. Genuine spares, and I show you the old part I replace."),
        new("Priya Nair", "priya.nair", "9810000004", "home-cleaning", "Gurugram", 5, "Deep cleaning for kitchens, bathrooms and full homes",
            "Kitchen degreasing, Bathroom descaling, Sofa shampooing, Move-in cleaning", "DLF Phase 1-5, Sohna Road, Golf Course Road",
            VerificationStatus.Approved, 8, 17,
            "I lead a two-person cleaning team using eco-friendly products that are safe for children and pets."),
        new("Harpreet Singh", "harpreet.singh", "9810000005", "carpentry", "New Delhi", 15, "Custom furniture, repairs and modular fittings",
            "Furniture repair, Modular kitchen fitting, Door and lock work, Custom shelves", "Janakpuri, Dwarka, Vikaspuri, Uttam Nagar",
            VerificationStatus.Approved, 9, 18,
            "Fifteen years of carpentry, from fixing a squeaky door to building a full wardrobe. I bring my own power tools and clean up the sawdust."),
        new("Deepak Kumar", "deepak.kumar", "9810000006", "painting", "Noida", 10, "Interior and exterior painting with a clean finish",
            "Interior emulsion, Texture walls, Waterproofing, Wood polish", "Sector 50, Sector 76, Sector 128, Indirapuram",
            VerificationStatus.Approved, 9, 18,
            "My team covers furniture and floors before we start and finishes on the agreed date. Free colour consultation with every room."),
        new("Ananya Iyer", "ananya.iyer", "9810000007", "tutoring", "Bengaluru", 6, "Maths and science tutor for classes 6-12",
            "CBSE Maths, ICSE Physics, Exam strategy, Olympiad basics", "Koramangala, HSR Layout, Indiranagar, Online",
            VerificationStatus.Approved, 16, 21,
            "M.Sc. in Physics. I focus on understanding over memorising and share a short progress note with parents after every session."),
        new("Neha Kapoor", "neha.kapoor", "9810000008", "beauty-salon", "New Delhi", 8, "Salon-quality beauty services at home",
            "Haircuts, Facials, Party makeup, Threading and waxing", "Greater Kailash, Defence Colony, Lajpat Nagar",
            VerificationStatus.Approved, 10, 19,
            "Certified beautician with eight years of salon experience. I use single-use kits and branded products only."),
        new("Vikram Joshi", "vikram.joshi", "9810000009", "appliance-repair", "Mumbai", 11, "Washing machine, refrigerator and microwave repair",
            "Washing machines, Refrigerators, Microwaves, Water purifiers", "Andheri, Powai, Goregaon, Malad",
            VerificationStatus.Approved, 9, 19,
            "Eleven years repairing home appliances of all major brands. 30-day warranty on every repair."),
        new("Sanjay Gupta", "sanjay.gupta", "9810000010", "pest-control", "Gurugram", 9, "Odourless pest control for homes and offices",
            "Cockroach gel, Termite treatment, Bed bugs, Mosquito fogging", "Sector 45, Sector 56, South City, Palam Vihar",
            VerificationStatus.Approved, 9, 18,
            "Government-approved chemicals applied by trained technicians. Kitchens can be used again two hours after treatment."),
        new("Kavita Rao", "kavita.rao", "9810000011", "tutoring", "New Delhi", 4, "Spoken English and grammar coach",
            "Spoken English, Grammar, Interview preparation, IELTS speaking", "Saket, Vasant Kunj, Online",
            VerificationStatus.Approved, 16, 21,
            "I help students and professionals speak English with confidence through short, practical sessions."),
        new("Amit Chauhan", "amit.chauhan", "9810000012", "plumbing", "Gurugram", 6, "Quick plumbing repairs and installations",
            "Geyser installation, Leak repair, Motor fitting, Bathroom fittings", "Sector 14, Sector 21, DLF Phase 3",
            VerificationStatus.Approved, 9, 18,
            "Fast, tidy plumbing for apartments and independent houses across Gurugram."),
        new("Farhan Sheikh", "farhan.sheikh", "9810000013", "ac-repair", "Mumbai", 8, "AC servicing and repair across the western suburbs",
            "Split AC service, Window AC repair, Gas refill, Installation", "Bandra, Khar, Santacruz, Andheri",
            VerificationStatus.Approved, 10, 19,
            "Same-day AC servicing in the western suburbs with transparent pricing."),
        new("Meera Pillai", "meera.pillai", "9810000014", "home-cleaning", "Bengaluru", 3, "Reliable home and office cleaning",
            "Home cleaning, Office cleaning, Kitchen cleaning, Window cleaning", "Whitefield, Marathahalli, Bellandur",
            VerificationStatus.Approved, 8, 17,
            "Careful, punctual cleaning with my own equipment and supplies."),
        new("Imran Qureshi", "imran.qureshi", "9810000015", "electrical", "Noida", 5, "Electrical repairs and new connections",
            "Wiring, Lighting design, Meter connections, Earthing", "Sector 15, Sector 22, Sector 93",
            VerificationStatus.Pending, 9, 18,
            "ITI-certified electrician with five years of experience in residential projects."),
        new("Lakshmi Menon", "lakshmi.menon", "9810000016", "beauty-salon", "Bengaluru", 2, "Bridal and party makeup artist",
            "Bridal makeup, Hair styling, Saree draping", "Jayanagar, JP Nagar, Banashankari",
            VerificationStatus.Pending, 10, 19,
            "Makeup artist trained at a professional academy, specialising in South Indian bridal looks."),
        new("Rajesh Thakur", "rajesh.thakur", "9810000017", "painting", "New Delhi", 1, "", "", "",
            VerificationStatus.NotSubmitted, 9, 18, "")
    ];

    public record DemoCustomer(string FullName, string Email, string Phone, string City, string Address);

    public static readonly DemoCustomer[] Customers =
    [
        new("Aarav Sharma", "aarav.sharma", "9899000001", "New Delhi", "C-42, Lajpat Nagar II, New Delhi 110024"),
        new("Isha Malhotra", "isha.malhotra", "9899000002", "Gurugram", "Tower 5, Flat 1102, DLF Park Place, Gurugram 122002"),
        new("Kabir Mehta", "kabir.mehta", "9899000003", "Noida", "B-17, Sector 50, Noida 201301"),
        new("Sneha Reddy", "sneha.reddy", "9899000004", "Bengaluru", "No. 14, 5th Cross, Koramangala 4th Block, Bengaluru 560034"),
        new("Rahul Bansal", "rahul.bansal", "9899000005", "Mumbai", "702, Sea Breeze, Juhu Tara Road, Mumbai 400049"),
        new("Pooja Saxena", "pooja.saxena", "9899000006", "New Delhi", "H-8, Saket, New Delhi 110017"),
        new("Ishaan Kapoor", "ishaan.kapoor", "9899000007", "Gurugram", "House 311, Sector 46, Gurugram 122003"),
        new("Ananya Gupta", "ananya.gupta", "9899000008", "Noida", "A-904, Supertech Capetown, Sector 74, Noida 201301"),
        new("Rohan Desai", "rohan.desai", "9899000009", "Mumbai", "12, Hill Road, Bandra West, Mumbai 400050"),
        new("Meghna Iyer", "meghna.iyer", "9899000010", "Bengaluru", "88, 12th Main, HSR Layout Sector 6, Bengaluru 560102"),
        new("Vivek Arora", "vivek.arora", "9899000011", "New Delhi", "B-2/14, Safdarjung Enclave, New Delhi 110029"),
        new("Tanvi Joshi", "tanvi.joshi", "9899000012", "Noida", "C-55, Sector 27, Noida 201301"),
        new("Siddharth Rao", "siddharth.rao", "9899000013", "Bengaluru", "4th Floor, 21 Indiranagar 100 Feet Road, Bengaluru 560038"),
        new("Nidhi Bhatt", "nidhi.bhatt", "9899000014", "Mumbai", "1802, Lodha Bellissimo, Mahalaxmi, Mumbai 400011")
    ];

    /// <summary>Review comments grouped by star rating, so the text always matches the stars.</summary>
    public static readonly Dictionary<int, string[]> ReviewComments = new()
    {
        [5] =
        [
            "Arrived on time, explained the problem before starting and finished neatly. Fair pricing and no surprises on the bill.",
            "Very professional. Fixed the issue in one visit and cleaned up afterwards. Already recommended to my neighbours.",
            "Excellent service. Everything was done exactly as promised, and I got tips to avoid the problem in future.",
            "Best home-service experience I've had. Transparent, punctual and very clean work.",
            "Really knowledgeable and careful inside the house. My parents felt comfortable, which matters a lot to us.",
            "Came 10 minutes early, wore shoe covers and finished faster than estimated. Will book again.",
            "Polite, quick and the price matched the listing exactly. Five stars.",
            "Booked late at night for the next morning and it was confirmed within minutes. Great work.",
            "Patient and thorough. Checked everything twice before leaving.",
            "Our second booking with them and just as good as the first."
        ],
        [4] =
        [
            "Good work and friendly behaviour. Would book again for the next job.",
            "Solid job at a fair price. Slightly rushed at the end but everything works perfectly.",
            "Took the time to understand what we needed and suggested a cheaper fix instead of replacing everything.",
            "Skilled and polite. Needed a short trip to buy a part, which added about half an hour.",
            "Quick and tidy. Booking a slot that suited me was easy.",
            "Work quality was very good. Communication before the visit could have been a little better.",
            "Did what was asked without fuss. Reasonable price for the area.",
            "Happy with the result. Arrived about 15 minutes after the slot started."
        ],
        [3] =
        [
            "Decent job overall, but they arrived 25 minutes late and I had to remind them about one small thing.",
            "The work itself was fine but took far longer than the estimate.",
            "Okay service. Had to call twice to confirm the address.",
            "Average. The problem is fixed for now, but I'm not sure it will last.",
            "Friendly, but left some mess behind that we had to clean up.",
            "Got the job done. Charged a little extra for materials that wasn't mentioned up front."
        ],
        [2] =
        [
            "Arrived an hour late without calling. The fix worked but the experience was frustrating.",
            "Had to book a second visit because the first one didn't solve the problem.",
            "Seemed in a hurry and didn't explain what was done.",
            "Overall not great. The price went up once they were here."
        ],
        [1] =
        [
            "Didn't turn up in the slot and arrived the next day instead. Would not book again.",
            "The issue came back the same evening and they didn't respond to messages.",
            "Rude when I asked questions about the charges."
        ]
    };
}
