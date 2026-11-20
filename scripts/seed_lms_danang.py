#!/usr/bin/env python3
"""Seed the running local LMS Docker project with a linked Da Nang demo cohort."""
import base64
import datetime as dt
import getpass
import hashlib
import os
import subprocess
import sys
import uuid
from collections import defaultdict

PROJECT = "toeic-space-lms-be"
SERVICES = ("identity", "classroom")
TEACHERS = ["Trần Khánh Linh", "Lê Hải Nam", "Đỗ Ngọc Khang", "Phan Quang Vinh"]
STUDENTS = ["Phạm Thu Hà", "Hoàng Anh Thư", "Đặng Quốc Huy", "Võ Thanh Tâm",
            "Bùi Gia Hân", "Ngô Đức Phúc"]
STUDENTS += [f"{surname} {given}" for surname in ("Nguyễn", "Trần", "Lê", "Phan", "Đỗ", "Huỳnh")
             for given in ("Minh Châu", "Khánh An", "Anh Khoa", "Ngọc Anh",
                           "Gia Bảo", "Thảo Vy", "Thanh Phong")]
# Keep the original six learners in their original classes on an upgrade.
COHORTS = [
    ("Hải Châu", "đường Bạch Đằng", 550, [1, 2, 3, *range(7, 16)]),
    ("Sơn Trà", "đường Ngô Quyền", 750, [4, 5, 6, *range(16, 25)]),
    ("Thanh Khê", "đường Điện Biên Phủ", 650, range(25, 37)),
    ("Ngũ Hành Sơn", "đường Lê Văn Hiến", 850, range(37, 49)),
]


def uid(key):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "urn:toeic-space-lms:danang:" + key))


def password_hash(password):
    salt = os.urandom(16)
    digest = hashlib.pbkdf2_hmac("sha512", password.encode(), salt, 210000, 32)
    return "PBKDF2-SHA512$210000$" + "$".join(base64.b64encode(v).decode() for v in (salt, digest))


def dataset(password, monday):
    rows = defaultdict(list)
    created = str(monday - dt.timedelta(days=7))
    stamp = dict(CreatedAt=created, UpdatedAt=created)
    admin = uid("admin")

    def add(service, table, key, **values):
        rows[service].append((table, key.split(), values))

    accounts = [("admin", "Admin", "Nguyễn Bảo Nguyên")]
    accounts.extend((f"teacher{i}", "Teacher", name) for i, name in enumerate(TEACHERS, 1))
    accounts.extend((f"student{i}", "User", name) for i, name in enumerate(STUDENTS, 1))
    for alias, role, name in accounts:
        person = uid(alias)
        email = alias + "@danang.example"
        add("identity", "Users", "Id", Id=person, FullName=name, Email=email, Role=role,
            Status="Active", EmailVerifiedAt=created, PasswordHash=password_hash(password),
            CreatedAt=created, Version=1)
        if role != "Admin":
            teacher = role == "Teacher"
            key = "TeacherId" if teacher else "StudentId"
            fields = {key: person} if teacher else {key: person, "IsVerified": 1}
            add("classroom", "TeacherReadModels" if teacher else "StudentReadModels", key,
                **fields, Name=name, Email=email, Role=role, Status="Active", Version=1,
                IsEligible=1, LastEventId=uid("projection-" + alias), SynchronizedAt=created)

    for i, (area, street, target, students) in enumerate(COHORTS, 1):
        center, room, cls, schedule, teacher = (uid(f"{key}{i}") for key in
                                                ("center", "room", "class", "schedule", "teacher"))
        title = f"TOEIC {target}+ · {area}"
        add("classroom", "Centers", "Id", Id=center, Name="TOEICSpace " + area,
            Address=f"{street}, {area}, Đà Nẵng", TimeZoneId="Asia/Ho_Chi_Minh", Status="Active", **stamp)
        add("classroom", "Rooms", "Id", Id=room, CenterId=center, Name=f"DN-{i}01", Capacity=20,
            Status="Active", **stamp)
        add("classroom", "Classes", "Id", Id=cls, Code=f"DN-DEMO-{target}", Name=title,
            DefaultRoomId=room, StartDate=monday, EndDate=monday + dt.timedelta(days=27),
            CreatedBy=admin, UpdatedBy=admin, Version=1, Status="Active", **stamp)
        add("classroom", "ClassInstructors", "Id", Id=uid(f"instructor{i}"), ClassId=cls,
            TeacherId=teacher, AssignedBy=admin, AssignedAt=created)
        add("classroom", "ClassSchedules", "Id", Id=schedule, ClassId=cls, TeacherId=teacher,
            DaysOfWeek="[1]", StartTime="18:00:00", EndTime="19:30:00", Mode="Offline",
            EffectiveFrom=monday, EffectiveTo=monday + dt.timedelta(days=27),
            TimeZoneId="Asia/Ho_Chi_Minh", ExcludeDates="[]", Version=1, **stamp)
        for week in range(4):
            day = monday + dt.timedelta(weeks=week)
            start, end = f"{day} 11:00:00", f"{day} 12:30:00"
            done = week == 0
            session = uid(f"session{i}-{week}")
            add("classroom", "ClassSessions", "Id", Id=session, ClassId=cls, ClassScheduleId=schedule,
                OriginalLocalDate=day, TeacherId=teacher, Title=f"{title} · Buổi {week+1}",
                Mode="Offline", RoomId=room, SessionType="Regular", StartAt=start, EndAt=end,
                Status="Completed" if done else "Scheduled", CompletedAt=end if done else None,
                TimeOverridden=0, RoomOverridden=0, ModeOverridden=0, TeacherOverridden=0,
                Version=1, **stamp)
            if done:
                for j, number in enumerate(students):
                    student = uid(f"student{number}")
                    add("classroom", "AttendanceRecord", "Id", Id=uid(f"attendance{i}-{week}-{j}"),
                        ClassSessionId=session, StudentId=student, Status=("Present", "Late", "Absent", "Excused")[j] if j < 4 else "Present",
                        RecordedBy=teacher, RecordedAt=end, UpdatedAt=end)
        for number in students:
            student = uid(f"student{number}")
            enrollment = uid(f"enrollment{number}")
            add("classroom", "ClassEnrollments", "Id", Id=enrollment, ClassId=cls, StudentId=student,
                Status="Active", EnrolledBy=admin, EnrolledAt=created)
    return rows


def literal(value):
    if value is None:
        return "NULL"
    if isinstance(value, int):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def statements(rows):
    yield "START TRANSACTION;"
    for table, keys, row in rows:
        columns = ", ".join(f"`{k}`" for k in row)
        values = ", ".join(map(literal, row.values()))
        condition = " AND ".join(f"`{k}`={literal(row[k])}" for k in keys)
        yield (f"INSERT INTO `{table}` ({columns}) SELECT {values} WHERE NOT EXISTS "
               f"(SELECT 1 FROM `{table}` WHERE {condition});")
    yield "COMMIT;"


def main():
    commands = {}
    for service in SERVICES:
        ids = subprocess.check_output(["docker", "ps", "-q", "--filter", f"label=com.docker.compose.project={PROJECT}",
                                       "--filter", f"label=com.docker.compose.service={service}-db"], text=True).split()
        if len(ids) != 1:
            sys.exit(f"Cần khởi động Docker LMS và chạy migration cho {service}-db trước khi seed.")
        commands[service] = ["docker", "exec", "-i", ids[0], "sh", "-ec",
                             'export MYSQL_PWD="$MYSQL_PASSWORD"; exec mysql --default-character-set=utf8mb4 '
                             '--protocol=TCP -h 127.0.0.1 -u "$MYSQL_USER" --database="$MYSQL_DATABASE"']
        subprocess.run(commands[service], input="SELECT COUNT(*) INTO @m FROM __EFMigrationsHistory;", text=True, check=True)
    password = getpass.getpass("Đặt mật khẩu cho các tài khoản demo mới (ít nhất 12 ký tự): ")
    if len(password) < 12:
        sys.exit("Mật khẩu cần ít nhất 12 ký tự.")
    today = dt.datetime.now(dt.timezone(dt.timedelta(hours=7))).date()
    # Reuse the original calendar when adding people to an existing demo.
    query = f"SELECT StartDate FROM Classes WHERE Id='{uid('class1')}';"
    existing = subprocess.check_output(commands["classroom"] + ["-N", "-B"],
                                       input=query, text=True).strip()
    monday = dt.date.fromisoformat(existing) if existing else today - dt.timedelta(days=today.weekday() + 7)
    rows = dataset(password, monday)
    for service in SERVICES:
        subprocess.run(commands[service], input="\n".join(statements(rows[service])), text=True, check=True)
        print(f"{service}: hoàn tất")


if __name__ == "__main__":
    main()
