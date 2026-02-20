from fastapi import APIRouter, Depends, Body
from fastapi.responses import JSONResponse
from typing import Union, Annotated
from sqlmodel import Session
from db.session import get_session
from service.user_service import UserService
from schemas.user import CreateUserRequest
import logging as log

router = APIRouter()

@router.get("/")
async def read_root(session: Session = Depends(get_session)):
    return UserService().get_all(session)

@router.get("/items/{item_id}")
async def read_item(item_id: int, q: Union[str, None] = None, session: Session = Depends(get_session)):
    return {"item_id": item_id, "q": q}

@router.put("/user/create")
async def create_user(user_data: Annotated[CreateUserRequest, Body(embed=True)], session: Session = Depends(get_session)):
    log.info("[create_user] Creating user with data: %s", user_data)
    try:
        return UserService().create_user(session, user_data)
    except Exception as e:
        log.error("[create_user] Error creating user: %s", e)
        log.exception(e)
        return JSONResponse(status_code=400, content={"success": False, "error": str(e)})
    